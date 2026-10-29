using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class ClientPortalTests
{
 sealed class State:AuthenticationStateProvider{public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,"owner"),new(ClaimTypes.NameIdentifier,"owner"),new(ClaimTypes.Role,"Recruiter")],"Test"))));}
 sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
 sealed class Mail:IAccountEmail{public bool Configured=>true;public Task Send(string email,string subject,string body)=>Task.CompletedTask;}
 sealed class Fixture:IAsyncDisposable
 {
  readonly SqliteConnection connection=new("Data Source=:memory:");public Factory Factory=null!;public Access Access=null!;public HiringReviewService Review=null!;public WorkspaceService Workspace=null!;public Guid WorkspaceId;public Client Client=new(){Name="Client",NormalizedName="CLIENT"};public ClientContact Contact=new(){Name="Manager",Email="manager@example.test"};public Position Position=null!;public Assessment App=null!;
  public async Task Init(string plan="starter"){
   await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);
   db.Users.Add(new(){Id="owner",UserName="owner",AccountType=AccountTypes.Freelance,EmailConfirmed=true});db.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole("Recruiter"){Id="recruiter"});db.UserRoles.Add(new(){UserId="owner",RoleId="recruiter"});
   var w=new EmployerWorkspace{OwnerId="owner",Kind=AccountTypes.Freelance,CompanyName="Owner"};SubscriptionRules.Apply(w,await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Freelancer"&&p.Code==plan));db.EmployerWorkspaces.Add(w);WorkspaceId=w.Id;
   Client.WorkspaceId=w.Id;Contact.ClientId=Client.Id;Client.Contacts.Add(Contact);db.Clients.Add(Client);Position=new(){ClientId=Client.Id,Client=Client.Name,WorkspaceId=w.Id,Reference="101",Title="Position",AssignedRecruiter="owner"};db.Positions.Add(Position);
   var candidate=new Candidate{Name="Private Candidate",Email="candidate@private.test",Phone="+923001234567",CurrentRole="Analyst"};db.Candidates.Add(candidate);App=new(){CandidateId=candidate.Id,PositionId=Position.Id,CandidateName=candidate.Name,State="Saved",Stage="Shortlisted",SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(candidate,5,[],"Evidence","Strength","Gap",Client.Name,Position.Title,Position.Reference))};db.Assessments.Add(App);await db.SaveChangesAsync();
   Access=new(new State(),Factory);Review=new(Factory,Access,new EphemeralDataProtectionProvider(),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Review:PublicBaseUrl","https://ats.example.test"}}).Build(),new Mail(),new RecruitmentUpdates());Workspace=new(Factory,Access,Review);
  }
  public async Task<string> Cookie(string link){var token=QueryHelpers.ParseQuery(new Uri(link).Query)["token"].ToString();return (await Review.Exchange(token))!.Value.Cookie;}
  public ValueTask DisposeAsync()=>connection.DisposeAsync();
 }
 [Fact]public async Task FreeSubmissionQueuesOnlyAnActivityCountAndDoesNotCreatePortalAccess(){
  await using var f=new Fixture();await f.Init("free");var result=await f.Review.Forward(null,[f.App.Id],"",f.Contact.Email);Assert.Empty(result.Link);
  await using var db=f.Factory.CreateDbContext();Assert.Empty(await db.HiringReviewGrants.ToListAsync());Assert.Empty(await db.HiringSubmissions.ToListAsync());var delivery=Assert.Single(await db.PortalEmailDeliveries.ToListAsync());Assert.DoesNotContain("Private Candidate",delivery.Body);Assert.DoesNotContain("candidate@",delivery.Body);Assert.DoesNotContain("/review",delivery.Body);Assert.False(delivery.RequiresPublicAddress);Assert.Contains("1 profile",delivery.Body);Assert.Equal("Awaiting Employer Review",(await db.Assessments.SingleAsync()).Stage);
 }
 [Fact]public async Task FreeContactDetailsRemainEditableButPortalOptInIsRejected(){
  await using var f=new Fixture();await f.Init("free");var client=Assert.Single(await f.Workspace.Clients());var id=client.Contacts[0].Id;client.Contacts[0].Title="Head of HR";await f.Workspace.SaveClient(client);client=Assert.Single(await f.Workspace.Clients());Assert.Equal(id,client.Contacts[0].Id);client.Contacts[0].PortalEnabled=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspace.SaveClient(client));await using var db=f.Factory.CreateDbContext();Assert.False((await db.ClientContacts.SingleAsync()).PortalEnabled);Assert.Empty(await db.HiringReviewGrants.ToListAsync());
 }
 [Fact]public async Task PaidContactProvisioningPreservesIdentityAndRevokingItEndsExistingSessions(){
  await using var f=new Fixture();await f.Init();var client=Assert.Single(await f.Workspace.Clients());client.Contacts[0].PortalEnabled=true;await f.Workspace.SaveClient(client);var enabled=await f.Review.PortalAccess(client.Id,null,f.Contact.Id);Assert.NotNull(enabled.Link);var result=await f.Review.Forward(f.Contact.Id,[f.App.Id],"",false);var cookie=await f.Cookie(result.Link);Assert.Single((await f.Review.Read(cookie)).Rows);
  client=Assert.Single(await f.Workspace.Clients());client.Contacts[0].Title="HR Lead";await f.Workspace.SaveClient(client);Assert.Single((await f.Review.Read(cookie)).Rows);client=Assert.Single(await f.Workspace.Clients());client.Contacts[0].PortalEnabled=false;await f.Workspace.SaveClient(client);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Read(cookie));await using var db=f.Factory.CreateDbContext();Assert.Single(await db.HiringSubmissions.ToListAsync());Assert.Equal(f.Contact.Id,(await db.ClientContacts.SingleAsync()).Id);
 }
 [Fact]public async Task PositionPortalCannotReadOtherPositionsEvenWithTheSameClient(){
  await using var f=new Fixture();await f.Init();await using(var db=f.Factory.CreateDbContext()){var p=await db.Positions.SingleAsync();p.HiringManagerPortalEnabled=true;p.HiringContact="Manager";await f.Review.ConfigurePosition(db,p);await db.SaveChangesAsync();}
  var forwarded=await f.Review.Forward(null,[f.App.Id],"",(string?)null);var cookie=await f.Cookie(forwarded.Link);Guid otherId;
  await using(var db=f.Factory.CreateDbContext()){var other=new Position{ClientId=f.Client.Id,Client=f.Client.Name,WorkspaceId=f.WorkspaceId,Reference="102",Title="Other Position",AssignedRecruiter="owner"};db.Positions.Add(other);otherId=other.Id;await db.SaveChangesAsync();}
  var result=await f.Review.Read(cookie,otherId);Assert.DoesNotContain(result.Positions,p=>p.Id==otherId);Assert.All(result.Rows,r=>Assert.Equal(f.Position.Id,r.PositionId));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.PortalSend(cookie,new(otherId,"Leak test")));await f.Review.PortalSend(cookie,new(f.Position.Id,"Please confirm availability"));Assert.Single(await f.Review.PortalInbox());var thread=Assert.Single(await f.Review.PortalInbox());await f.Review.PortalReply(thread.Id,f.Position.Id,"Available next week candidate@private.test");var messages=await f.Review.PortalMessages(cookie);Assert.Equal(2,messages.Messages.Count);Assert.DoesNotContain("candidate@private.test",messages.Messages.Last().Body);await using var check=f.Factory.CreateDbContext();Assert.Equal(1,await check.Users.CountAsync());
 }
 [Fact]public async Task DowngradeBlocksExistingPortalCookiesAndDoesNotRestoreAnAccountRole(){
  await using var f=new Fixture();await f.Init();var client=Assert.Single(await f.Workspace.Clients());client.Contacts[0].PortalEnabled=true;await f.Workspace.SaveClient(client);var cookie=await f.Cookie((await f.Review.Forward(f.Contact.Id,[f.App.Id],"",false)).Link);
  await using(var db=f.Factory.CreateDbContext()){var w=await db.EmployerWorkspaces.SingleAsync();SubscriptionRules.Apply(w,await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Freelancer"&&p.Code=="free"));await db.SaveChangesAsync();}
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Read(cookie));await using var check=f.Factory.CreateDbContext();Assert.Equal(1,await check.Users.CountAsync());Assert.Equal(1,await check.UserRoles.CountAsync());
 }
 [Fact]public async Task PortalConversionPreservesExistingLinkTokensSubmissionsAndContactIds(){
  await using var f=new Fixture();await f.Init();await f.Review.ManageClientLink(f.Client.Id,true);var link=await f.Review.Forward(f.Contact.Id,[f.App.Id],"",false);var cookie=await f.Cookie(link.Link);string hash;
  await using(var db=f.Factory.CreateDbContext()){hash=(await db.HiringReviewGrants.SingleAsync()).TokenHash;var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261026120000_AdminPaymentDetails");await migrator.MigrateAsync();}
  Assert.Single((await f.Review.Read(cookie)).Rows);await using var check=f.Factory.CreateDbContext();Assert.Equal(hash,(await check.HiringReviewGrants.SingleAsync()).TokenHash);Assert.Equal(f.Contact.Id,(await check.ClientContacts.SingleAsync()).Id);Assert.True((await check.ClientContacts.SingleAsync()).PortalEnabled);Assert.Equal(1,await check.HiringSubmissions.CountAsync());
 }
 [Fact]public async Task LegacyEmployerLinkHonorsThePositionPortalCheckbox(){
  await using var f=new Fixture();await f.Init();await using(var db=f.Factory.CreateDbContext()){(await db.EmployerWorkspaces.SingleAsync()).Kind=AccountTypes.Employer;(await db.Positions.SingleAsync()).HiringManagerPortalEnabled=true;await db.SaveChangesAsync();}
  await f.Review.ManageClientLink(f.Client.Id,true);var link=await f.Review.Forward(f.Contact.Id,[f.App.Id],"",false);var cookie=await f.Cookie(link.Link);Assert.Single((await f.Review.Read(cookie)).Rows);Assert.NotNull((await f.Review.PortalAccess(f.Client.Id,f.Position.Id)).Link);
  await using(var db=f.Factory.CreateDbContext()){(await db.Positions.SingleAsync()).HiringManagerPortalEnabled=false;await db.SaveChangesAsync();}
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Review.Read(cookie));
 }
 [Fact]public async Task NotificationOnlySubmissionCanMoveToPortalReviewWithoutDuplicateReporting(){
  await using var f=new Fixture();await f.Init("free");await f.Review.Forward(null,[f.App.Id],"",f.Contact.Email);Assert.Equal("Notification only",Assert.Single(await f.Review.Inbox()).Delivery);
  await using(var db=f.Factory.CreateDbContext()){SubscriptionRules.Apply(await db.EmployerWorkspaces.SingleAsync(),await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Freelancer"&&p.Code=="starter"));await db.SaveChangesAsync();}
  var client=Assert.Single(await f.Workspace.Clients());client.Contacts[0].PortalEnabled=true;await f.Workspace.SaveClient(client);var result=await f.Review.Forward(f.Contact.Id,[f.App.Id],"",false);Assert.NotEmpty(result.Link);Assert.NotEqual("Notification only",Assert.Single(await f.Review.Inbox()).Delivery);
  var work=await new RecruiterWorkService(new State(),f.Factory,new EphemeralDataProtectionProvider()).Overview();Assert.Single(work.Submissions);await using var check=f.Factory.CreateDbContext();Assert.Equal(1,await check.EmployerProfileSubmissions.CountAsync());Assert.Equal(1,await check.HiringSubmissions.CountAsync());
 }
}
