using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;
namespace Osynix.Ats.Tests;
public class HiringReviewTests {
 sealed class State(string role="Admin"):AuthenticationStateProvider{
  public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,"recruiter"),new(ClaimTypes.NameIdentifier,"recruiter"),new(ClaimTypes.Role,role)],"Test"))));
 }
 sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
 sealed class Mail:IAccountEmail{public bool Configured=>true;public List<string> Bodies=[];public Task Send(string email,string subject,string body){Bodies.Add(body);return Task.CompletedTask;}}
 sealed class Env:IWebHostEnvironment{public string ApplicationName{get;set;}="Test";public string EnvironmentName{get;set;}="Development";public string ContentRootPath{get;set;}=Path.GetTempPath();public string WebRootPath{get;set;}=Path.GetTempPath();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
 sealed class Fixture:IAsyncDisposable{
  public SqliteConnection Db=new("Data Source=:memory:");public Factory Factory=null!;public Mail Mail=new();public RecruitmentUpdates Updates=new();public HiringReviewService Review=null!;public ReportService Reports=null!;public Client Client=new(){Name="Client",NormalizedName="CLIENT"};public ClientContact Contact=new(){Name="Hiring Contact",Email="manager@client.test"};public Position Position=null!;public List<Guid> Ids=[];
  public async Task Init(int count=10){
   await Db.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(Db).Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);
   Contact.ClientId=Client.Id;Position=new(){Reference="001",Title="First Position",Client=Client.Name,ClientId=Client.Id,AssignedRecruiter="recruiter"};
   db.Clients.Add(Client);db.ClientContacts.Add(Contact);db.Positions.Add(Position);db.Users.Add(new(){Id="recruiter",UserName="recruiter"});db.Roles.Add(new IdentityRole("Recruiter"){Id="r"});db.UserRoles.Add(new(){UserId="recruiter",RoleId="r"});
   for(int i=0;i<count;i++){
    var c=new Candidate{Name="Candidate "+i,Email=$"candidate{i}@private.test",Phone="+923001234567",Summary="Summary",Notes="PRIVATE INTERNAL NOTE"};db.Candidates.Add(c);
    var a=new Assessment{PositionId=Position.Id,CandidateId=c.Id,State="Saved",CandidateName=c.Name,Stage="Shortlisted",Decision="Shortlist",MatchPercent=80,RecruiterNotes="PRIVATE RECRUITER NOTE",SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,5,[],$"Evidence. Contact {c.Email}, {c.Phone}, https://private.test/profile", "Strength","Gap",Client.Name,Position.Title,Position.Reference))};db.Assessments.Add(a);Ids.Add(a.Id);
   }
   await db.SaveChangesAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Review:PublicBaseUrl","https://ats.example.test"}}).Build();
   Review=new(Factory,new Access(new State("Recruiter"),Factory),new EphemeralDataProtectionProvider(),config,Mail,Updates);Reports=new(config,new Env());
  }
  public async Task<string> Cookie(ForwardResult result){var token=QueryHelpers.ParseQuery(new Uri(result.Link).Query)["token"].ToString();var exchange=await Review.Exchange(token);Assert.NotNull(exchange);return exchange.Value.Cookie;}
  public ValueTask DisposeAsync()=>Db.DisposeAsync();
 }
 [Fact]public async Task InformationFeedbackCreatesOneTaskAndAClientSafeResponseReturnsItForReview(){
  await using var f=new Fixture();await f.Init(1);var forwarded=await f.Review.Forward(f.Contact.Id,f.Ids,"",false);var cookie=await f.Cookie(forwarded);var row=Assert.Single((await f.Review.Read(cookie)).Rows);
  await f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,"More Information Required","Confirm availability",null,"","",30,""));
  var actions=new RecruiterActionService(f.Factory,new Access(new State("Recruiter"),f.Factory),f.Updates);var task=Assert.Single(await actions.Inbox());Assert.Equal("Respond To Client",task.Action.Type);Assert.Equal("recruiter",task.Action.RecipientId);
  await Assert.ThrowsAsync<InvalidOperationException>(()=>actions.Acknowledge(task.Action.Id,task.Action.Revision));
  await f.Review.RespondToInformation(task.Action.Id,task.Action.Revision,"Available next week. candidate0@private.test +923001234567");
  Assert.Empty(await actions.Inbox());row=Assert.Single((await f.Review.Read(cookie)).Rows);Assert.Equal("Awaiting Feedback",row.Feedback);Assert.Equal("Awaiting Client Feedback",row.Stage);Assert.Contains("Available next week",row.RecruiterResponse);Assert.DoesNotContain("@private.test",row.RecruiterResponse);Assert.DoesNotContain("923001234567",row.RecruiterResponse);
  await f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,"Shortlist","Suitable",null,"","",30,""));Assert.Empty(await actions.Inbox());task=Assert.Single(await actions.Notifications(false));Assert.Equal("Review Feedback",task.Action.Type);await actions.Acknowledge(task.Action.Id,task.Action.Revision);Assert.Empty(await actions.Inbox());
  row=Assert.Single((await f.Review.Read(cookie)).Rows);await f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,"Shortlist","Suitable",null,"","",30,""));Assert.Empty(await actions.Inbox());
  await using var db=f.Factory.CreateDbContext();Assert.Equal(1,await db.RecruiterActions.CountAsync());Assert.Equal(1,await db.HiringSubmissions.CountAsync());Assert.Equal("Shortlisted",(await db.Assessments.SingleAsync()).Stage);
 }
 [Fact]public async Task ReusableLinkCanBeFoundByClientContactAndPositionAndReplacementPreservesHistory(){
  await using var f=new Fixture();await f.Init(1);var forwarded=await f.Review.Forward(f.Contact.Id,f.Ids,"",false);var cookie=await f.Cookie(forwarded);
  Assert.Equal(forwarded.Link,(await f.Review.ClientLink(f.Client.Id)).Link);Assert.Equal(forwarded.Link,(await f.Review.ClientLink(f.Client.Id,null,f.Contact.Id)).Link);Assert.Contains("position="+f.Position.Id,(await f.Review.ClientLink(f.Client.Id,f.Position.Id)).Link);
  var admin=new HiringReviewService(f.Factory,new Access(new State()),new EphemeralDataProtectionProvider(),new ConfigurationBuilder().Build(),f.Mail,f.Updates);
  await admin.ManageClientLink(f.Client.Id,true);Assert.Null(await f.Review.Exchange(QueryHelpers.ParseQuery(new Uri(forwarded.Link).Query)["token"].ToString()));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Read(cookie));
  await using var db=f.Factory.CreateDbContext();Assert.Equal(1,await db.HiringSubmissions.CountAsync());await admin.ManageClientLink(f.Client.Id,false);Assert.Equal("Revoked",(await f.Review.ClientLink(f.Client.Id)).Status);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Review.Forward(f.Contact.Id,f.Ids,"",false));
 }
 [Fact]public async Task BackfillIsIdempotentAndDoesNotCountAvailabilityAsAnInterview(){
  await using var f=new Fixture();await f.Init(1);var forwarded=await f.Review.Forward(f.Contact.Id,f.Ids,"",false);
  await using(var db=f.Factory.CreateDbContext()){var sub=await db.HiringSubmissions.SingleAsync();sub.Feedback="Request Interview";sub.FeedbackUtc=DateTime.UtcNow;var app=await db.Assessments.SingleAsync();app.Stage="Client Interview";app.InterviewStatus="Not Scheduled";await db.SaveChangesAsync();await RecruiterActionService.Backfill(db);await RecruiterActionService.Backfill(db);Assert.Equal(1,await db.RecruiterActions.CountAsync());Assert.Equal("Interview Requested",app.Stage);Assert.Equal("Action Required",Lifecycle.Classify(app));var scheduled=Lifecycle.Synchronize(app.Stage,"Scheduled",app.ClientStatus);Assert.Equal("Client Interview",scheduled.Stage);}
  var access=new Access(new State("Recruiter"),f.Factory);var confirmedApp=Assert.Single(await new AtsService(f.Factory,access).Assessments());confirmedApp.InterviewStatus="Scheduled";await new AtsService(f.Factory,access).SaveLifecycle(confirmedApp);Assert.Empty(await new RecruiterActionService(f.Factory,access,f.Updates).Inbox());
 }
 [Theory][InlineData("Shortlist")][InlineData("Hold")][InlineData("Reject")]
 public async Task RoutineClientFeedbackIsAnUnreadNotificationRatherThanAnActionableTask(string status){
  await using var f=new Fixture();await f.Init(1);var link=await f.Review.Forward(f.Contact.Id,f.Ids,"",false);var cookie=await f.Cookie(link);var row=Assert.Single((await f.Review.Read(cookie)).Rows);await f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,status,"Client decision",null,"","",30,""));var actions=new RecruiterActionService(f.Factory,new Access(new State("Recruiter"),f.Factory),f.Updates);Assert.Empty(await actions.Inbox());var update=Assert.Single(await actions.Notifications(false));Assert.Equal(status,update.Action.Feedback);await actions.Acknowledge(update.Action.Id,update.Action.Revision);Assert.Empty(await actions.Notifications(false));if(status=="Reject")Assert.Empty((await f.Review.Read(cookie)).Rows);else Assert.Equal(status,Assert.Single((await f.Review.Read(cookie)).Rows).Feedback);
 }
 [Fact]public async Task LocalReviewLinkClearlyReportsEmailWasNotSent(){
  await using var f=new Fixture();await f.Init(1);
  f.Review=new(f.Factory,new Access(new State("Recruiter"),f.Factory),new EphemeralDataProtectionProvider(),new ConfigurationBuilder().Build(),f.Mail,f.Updates);
  var result=await f.Review.Forward(null,f.Ids,"","manager@example.test");
  Assert.Contains("Email Not Sent",result.Message);Assert.Contains("public HTTPS",result.Message);Assert.Empty(f.Mail.Bodies);
  await using var db=f.Factory.CreateDbContext();Assert.Equal("Email Not Sent",(await db.HiringSubmissions.SingleAsync()).Delivery);
 }
 [Fact]public async Task TenReportsAreContactFreeAndOneReusableLinkCoversOnlySharedPositions(){
  await using var f=new Fixture();await f.Init();int changes=0;f.Updates.Changed+=()=>changes++;
  var forwarded=await f.Review.Forward(f.Contact.Id,f.Ids,"Please review.",true);Assert.Single(f.Mail.Bodies);Assert.Contains("10 profiles",f.Mail.Bodies[0]);
  var cookie=await f.Cookie(forwarded);var data=await f.Review.Read(cookie);Assert.Equal(10,data.Rows.Count);
  var json=JsonSerializer.Serialize(data.Rows);Assert.DoesNotContain("@private.test",json);Assert.DoesNotContain("923001234567",json);Assert.DoesNotContain("PRIVATE",json);
  var html=f.Reports.HiringManagerHtml(await f.Review.Reports(cookie,null,f.Position.Id));Assert.Contains("Candidate Comparison",html);
  for(int i=0;i<10;i++){Assert.Contains("Candidate "+i,html);Assert.DoesNotContain($"candidate{i}@private.test",html);}
  Assert.DoesNotContain("923001234567",html);Assert.DoesNotContain("private.test/profile",html);Assert.DoesNotContain("PRIVATE RECRUITER NOTE",html);Assert.DoesNotContain("PRIVATE INTERNAL NOTE",html);
  Guid otherId,otherPositionId;
  await using(var db=f.Factory.CreateDbContext()){
   var p=new Position{Reference="002",Title="Second Position",Client=f.Client.Name,ClientId=f.Client.Id,AssignedRecruiter="recruiter"};db.Positions.Add(p);
   var c=new Candidate{Name="Second Candidate"};db.Candidates.Add(c);var a=new Assessment{State="Saved",PositionId=p.Id,CandidateId=c.Id,SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"","","",f.Client.Name,p.Title,p.Reference))};db.Assessments.Add(a);await db.SaveChangesAsync();otherId=a.Id;otherPositionId=p.Id;
  }
  Assert.Single((await f.Review.Read(cookie)).Rows.Select(r=>r.PositionId).Distinct());
  var second=await f.Review.Forward(f.Contact.Id,[otherId],"",false);Assert.Equal(forwarded.Link,second.Link);Assert.Equal(2,(await f.Review.Read(cookie)).Rows.Select(r=>r.PositionId).Distinct().Count());
  var first=(await f.Review.Read(cookie)).Rows.First(r=>r.PositionId==f.Position.Id);
  var response=new HiringFeedback(first.Id,first.Revision,first.ApplicationRevision,"Request Interview","Available online",DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm"),"UTC","Online",30,"");
  await f.Review.Reply(cookie,response);
  await using(var db=f.Factory.CreateDbContext()){var shared=await db.HiringSubmissions.SingleAsync(s=>s.Id==first.Id);var a=await db.Assessments.SingleAsync(a=>a.Id==shared.AssessmentId);Assert.Equal("Interview Requested",a.Stage);Assert.Equal("Not Scheduled",a.InterviewStatus);Assert.Equal("Action Required",Lifecycle.Classify(a));}
  Assert.Contains(await f.Review.Inbox(),r=>r.Feedback=="Request Interview"&&r.AvailabilityStartUtc is not null&&r.Mode=="Online");
  await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>f.Review.Reply(cookie,response));Assert.True(changes>=3);
  await f.Review.Revoke(first.Id);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Reports(cookie,first.Id,null));
  Assert.Equal(10,(await f.Review.Read(cookie)).Rows.Count);
 }
 [Fact]public async Task TokenExpiryClientBoundariesAndUnassignedRecordsCannotBypassSharing(){
  await using var f=new Fixture();await f.Init(1);
  var result=await f.Review.Forward(f.Contact.Id,f.Ids,"",false);var cookie=await f.Cookie(result);var row=Assert.Single((await f.Review.Read(cookie)).Rows);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Read("tampered"));
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Reports(cookie,Guid.NewGuid(),null));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,"Request Interview","",null,"UTC","Online",30,"")));
  await using(var db=f.Factory.CreateDbContext()){(await db.Positions.SingleAsync()).AssignedRecruiter="someone";await db.SaveChangesAsync();}
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Forward(f.Contact.Id,f.Ids,"",false));
  await using(var db=f.Factory.CreateDbContext()){(await db.Positions.SingleAsync()).ClientId=null;await db.SaveChangesAsync();}
  Assert.Empty((await f.Review.Read(cookie)).Rows);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,"Reject","",null,"","",30,"")));
  await using(var db=f.Factory.CreateDbContext()){(await db.HiringReviewGrants.SingleAsync()).ExpiresUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();}
  Assert.Null(await f.Review.Exchange(QueryHelpers.ParseQuery(new Uri(result.Link).Query)["token"].ToString()));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Read(cookie));
 }
 [Fact]public async Task ClientLinkIsReusableForContactsAndTypedEmailsWithoutSavingNewContacts(){
  await using var f=new Fixture();await f.Init(2);Guid secondContact;
  await using(var db=f.Factory.CreateDbContext()){
   (await db.ClientContacts.SingleAsync()).Email="";
   var other=new ClientContact{ClientId=f.Client.Id,Name="Second Hiring Contact",Email=""};db.ClientContacts.Add(other);await db.SaveChangesAsync();secondContact=other.Id;
  }
  var first=await f.Review.Forward(f.Contact.Id,[f.Ids[0]],"",false);
  var second=await f.Review.Forward(secondContact,[f.Ids[1]],"",false);
  Assert.Equal(first.Link,second.Link);Assert.Empty(f.Mail.Bodies);
  var firstCookie=await f.Cookie(first);var secondCookie=await f.Cookie(second);
  var clientRows=(await f.Review.Read(firstCookie)).Rows;Assert.Equal(2,clientRows.Count);
  var firstRow=clientRows.First();Assert.Equal(2,(await f.Review.Read(secondCookie)).Rows.Count);
  await f.Review.Reply(firstCookie,new(firstRow.Id,firstRow.Revision,firstRow.ApplicationRevision,"Shortlist","WhatsApp Link Feedback",null,"","",30,""));
  Assert.Contains(await f.Review.Inbox(),x=>x.Remarks=="WhatsApp Link Feedback");
  await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Review.Forward(f.Contact.Id,[f.Ids[0]],"",true));
  await using(var db=f.Factory.CreateDbContext()){db.ClientContacts.Remove(await db.ClientContacts.SingleAsync(c=>c.Id==f.Contact.Id));await db.SaveChangesAsync();}
  Assert.Equal(2,(await f.Review.Read(firstCookie)).Rows.Count);Assert.Equal(2,(await f.Review.Read(secondCookie)).Rows.Count);
  var typed=await f.Review.Forward(null,[f.Ids[0]],"Review", "new.manager@example.test");Assert.Equal(first.Link,typed.Link);Assert.Single(f.Mail.Bodies);Assert.Contains(first.Link,f.Mail.Bodies[0]);
  await using(var db=f.Factory.CreateDbContext())Assert.Single(await db.ClientContacts.ToListAsync());
  var noEmail=await f.Review.Forward(null,[f.Ids[1]],"", "");Assert.Equal(first.Link,noEmail.Link);Assert.Single(f.Mail.Bodies);
 }
 [Fact]public async Task SharedClientLinksCannotReadOrSubmitFeedbackForAnotherClient(){
  await using var f=new Fixture();await f.Init(1);
  var first=await f.Review.Forward(null,f.Ids,"","");var firstCookie=await f.Cookie(first);
  Guid otherApplication;
  await using(var db=f.Factory.CreateDbContext()){
   var client=new Client{Name="Other Client",NormalizedName="OTHER CLIENT"};db.Clients.Add(client);
   var p=new Position{Reference="Other",Title="Other Client Position",ClientId=client.Id,Client=client.Name,AssignedRecruiter="recruiter"};db.Positions.Add(p);
   var c=new Candidate{Name="Other Client Candidate"};db.Candidates.Add(c);
   var a=new Assessment{State="Saved",PositionId=p.Id,CandidateId=c.Id,SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"","","",client.Name,p.Title,p.Reference))};db.Assessments.Add(a);await db.SaveChangesAsync();otherApplication=a.Id;
  }
  var second=await f.Review.Forward(null,[otherApplication],"","");Assert.NotEqual(first.Link,second.Link);
  var row=Assert.Single((await f.Review.Read(await f.Cookie(second))).Rows);
  Assert.Single((await f.Review.Read(firstCookie)).Rows);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Reports(firstCookie,row.Id,null));
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Reply(firstCookie,new(row.Id,row.Revision,row.ApplicationRevision,"Shortlist","",null,"","",30,"")));
 }
 [Fact]public async Task ExistingReviewTokensAndSubmissionsSurviveClientScopeMigration(){
  await using var f=new Fixture();await f.Init(1);var link=await f.Review.Forward(f.Contact.Id,f.Ids,"",false);
  await using(var db=f.Factory.CreateDbContext()){
   var grant=await db.HiringReviewGrants.SingleAsync();grant.EmailKey=f.Contact.Email;grant.Name=f.Contact.Name;
   (await db.Assessments.SingleAsync()).Stage="Shortlisted";await db.SaveChangesAsync();
   var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261014114934_HiringManagerReview");await migrator.MigrateAsync();
  }
  var rows=(await f.Review.Read(await f.Cookie(link))).Rows;Assert.Single(rows);Assert.Equal("Awaiting Client Feedback",rows[0].Stage);
  var forwarded=await f.Review.Forward(null,f.Ids,"","new.email@example.test");Assert.Equal(link.Link,forwarded.Link);
  await using var check=f.Factory.CreateDbContext();Assert.Equal(f.Client.Id,(await check.HiringReviewGrants.SingleAsync()).ClientId);
 }
 [Fact]public async Task FeedbackStatusesUpdateTheRecruitmentQueues(){
  await using var f=new Fixture();await f.Init(1);var cookie=await f.Cookie(await f.Review.Forward(f.Contact.Id,f.Ids,"",false));
  foreach(var pair in new[]{("Shortlist","Shortlisted"),("Hold","On Hold"),("Reject","Rejected")}){
   var row=Assert.Single((await f.Review.Read(cookie)).Rows);await f.Review.Reply(cookie,new(row.Id,row.Revision,row.ApplicationRevision,pair.Item1,"Client remark",null,"","",30,""));
   await using var db=f.Factory.CreateDbContext();Assert.Equal(pair.Item2,Lifecycle.Classify(await db.Assessments.SingleAsync()));
  }
 }
}
