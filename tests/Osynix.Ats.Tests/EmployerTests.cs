using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;
namespace Osynix.Ats.Tests;
public class EmployerTests
{
    sealed class State(string id,string role):AuthenticationStateProvider
    {public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,id),new(ClaimTypes.Role,role)],"Test"))));}
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class CvQueueSpy:ICvProfileExtractionQueue{public List<Guid> CandidateIds=[];public void Queue(Guid candidateId)=>CandidateIds.Add(candidateId);}
    sealed class Mail:IAccountEmail{public bool Configured=>true;public List<string> Messages=[];public Task Send(string email,string subject,string body){Messages.Add(body);return Task.CompletedTask;}}
    static string Token(ForwardResult invite)=>QueryHelpers.ParseQuery(new Uri(invite.Link).Query)["token"].ToString();
    static async Task<ClaimsPrincipal> Recruiter(string id="recruiter")=>(await new State(id,"Recruiter").GetAuthenticationStateAsync()).User;
    sealed class Fixture:IAsyncDisposable
    {
        readonly SqliteConnection connection=new("Data Source=:memory:");public Factory Factory=null!;readonly EphemeralDataProtectionProvider protection=new();public Mail Mail=new();
        public async Task Init(){await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);foreach(var role in new[]{"Employer","Admin","Recruiter"})db.Roles.Add(new IdentityRole(role){Id=role});foreach(var id in new[]{"first","second","admin","recruiter","recruiter2","recruiter3"}){db.Users.Add(new AtsUser{Id=id,UserName=id,FullName=id,Email=id+"@example.test",NormalizedEmail=(id+"@example.test").ToUpperInvariant(),EmailConfirmed=true});db.UserRoles.Add(new IdentityUserRole<string>{UserId=id,RoleId=id=="admin"?"Admin":id.StartsWith("recruiter")?"Recruiter":"Employer"});}await db.SaveChangesAsync();foreach(var ownerId in new[]{"first","second"}){var workspace=await EmployerService.EnsureWorkspace(db,await db.Users.SingleAsync(u=>u.Id==ownerId));var tracked=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==workspace.Id);tracked.AdditionalRecruiterSeats=1;}await db.SaveChangesAsync();}
        public EmployerService Service(string id,string role="Employer"){var state=new State(id,role);return new(state,new Access(state,Factory),Factory);}
        public EmployerTeamService Teams(string owner="first")=>new(Service(owner),Factory,protection,new ConfigurationBuilder().Build(),Mail);
        public Access RecruiterAccess(string id="recruiter")=>new(new State(id,"Recruiter"),Factory);
        public ValueTask DisposeAsync()=>connection.DisposeAsync();
    }
    [Fact]public async Task CareersPreviewShowsOnlyOwnersPublishedActiveJobsAndSavesLongAboutUs(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("first");var own=(await owner.Overview()).Workspace;var other=(await f.Service("second").Overview()).Workspace;
        own.AboutUs=new string('A',12000)+"\nCompany values and opportunities.";await owner.SaveCompany(own);
        Assert.Equal(own.AboutUs,(await owner.Overview()).Workspace.AboutUs);
        Assert.Null(await owner.PublicCompany(own.Id));
        await using(var db=f.Factory.CreateDbContext()){
            db.Positions.AddRange(new Position{WorkspaceId=own.Id,Reference="PV1",Title="Visible",CareersPublished=true,Status="Active"},new Position{WorkspaceId=own.Id,Reference="PV2",Title="Draft",CareersPublished=false,Status="Active"},new Position{WorkspaceId=own.Id,Reference="PV3",Title="Closed",CareersPublished=true,Status="Closed"},new Position{WorkspaceId=other.Id,Reference="PV4",Title="Other Company",CareersPublished=true,Status="Active"});await db.SaveChangesAsync();
        }
        Assert.Equal("Visible",Assert.Single(await owner.CareersPreviewJobs()).Title);
        Assert.Equal("Other Company",Assert.Single(await f.Service("second").CareersPreviewJobs()).Title);
    }
    [Fact]public async Task CareersBrandingPersistsOwnerSelectionsAndRejectsInvalidUploads(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("first");var w=(await owner.Overview()).Workspace;
        Assert.Equal(10,CareersBranding.Covers.Length);Assert.Equal(10,CareersBranding.Covers.Select(c=>c.Path).Distinct().Count());
        w.CareersColorScheme="Emerald";w.CareersCoverMode="Stock";w.CareersStockCover=10;w.CareersPublished=true;await owner.SaveCompany(w);w=(await owner.Overview()).Workspace;Assert.Equal("Emerald",w.CareersColorScheme);Assert.Equal(10,w.CareersStockCover);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("second").SaveCompany(w));
        w.CareersColorScheme="red; background:url(https://example.test)";await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.SaveCompany(w));w.CareersColorScheme="Ocean";
        w.CareersStockCover=11;await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.SaveCompany(w));w.CareersStockCover=1;
        w.CareersCoverMode="Upload";await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.SaveCompany(w));
        w.CareersCoverContent="<svg onload='alert(1)'></svg>"u8.ToArray();await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.SaveCompany(w));
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0lcAAAAASUVORK5CYII=");w.CareersCoverContent=png;w.CareersCoverContentType="text/html";await owner.SaveCompany(w);var saved=(await owner.PublicCompany(w.Id))!;Assert.Equal("image/png",saved.CareersCoverContentType);Assert.Equal(png,saved.CareersCoverContent);Assert.Equal("Ocean",saved.CareersColorScheme);
        Assert.Throws<InvalidOperationException>(()=>CareersBranding.ValidateCover(new byte[5*1024*1024+1]));
        w=(await owner.Overview()).Workspace;w.CareersCoverMode="None";w.CareersCoverContent=null;await owner.SaveCompany(w);Assert.Null((await owner.PublicCompany(w.Id))!.CareersCoverContent);
    }
    [Fact]public async Task PublicCareersOnlyReturnsExplicitActiveJobsFromPublishedCompany(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("first");var w=(await owner.Overview()).Workspace;var other=(await f.Service("second").Overview()).Workspace;
        await using(var db=f.Factory.CreateDbContext()){
            db.Positions.AddRange(new Position{WorkspaceId=w.Id,Reference="PUB",Title="Public Role",Client="Test Client",CareersPublished=true,PublicJobDescription="Reviewed Description",ClientNotes="PRIVATE",JobDescription="PRIVATE JD"},new Position{WorkspaceId=w.Id,Reference="DRAFT",Title="Draft",CareersPublished=true,Status="Draft"},new Position{WorkspaceId=w.Id,Reference="HIDDEN",Title="Hidden"},new Position{WorkspaceId=other.Id,Reference="OTHER",Title="Other Team",CareersPublished=true});await db.SaveChangesAsync();
        }
        Assert.Empty(await owner.PublicJobs(w.Id));w.CareersPublished=true;await owner.SaveCompany(w);var job=Assert.Single(await owner.PublicJobs(w.Id));Assert.Equal("Public Role",job.Title);Assert.Equal("Reviewed Description",job.Description);
        var ats=new AtsService(f.Factory,new Access(new State("first","Employer"),f.Factory));var position=(await ats.Positions()).Single(p=>p.Reference=="PUB");position.Criteria=[new(){Name="Experience",Weight=100}];position.Status="Closed";position.ClosureReason="Completed";await ats.SavePosition(position);Assert.Empty(await owner.PublicJobs(w.Id));Assert.False((await ats.Positions()).Single(p=>p.Reference=="PUB").CareersPublished);
        w=(await owner.Overview()).Workspace;w.CareersPublished=false;await owner.SaveCompany(w);Assert.Null(await owner.PublicCompany(w.Id));
    }
    [Fact]public async Task PublicApplicationCreatesPrivateCvAndAppliedPipelineRecordWithoutAnAccount(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("first");var w=(await owner.Overview()).Workspace;w.CareersPublished=true;await owner.SaveCompany(w);var position=new Position{WorkspaceId=w.Id,Reference="APPLY-01",Title="Public Role",Client="Company",CareersPublished=true,PublicJobDescription="Reviewed JD"};await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(position);await db.SaveChangesAsync();}
        var pdf="%PDF-1.7\nCV"u8.ToArray();var application=await owner.ApplyPublic(w.Id,position.Id,"Candidate Name","CANDIDATE@example.test","+92 300 1234567","Lahore","candidate.pdf",pdf);
        await using(var db=f.Factory.CreateDbContext()){var assessment=await db.Assessments.Include(x=>x.Candidate).SingleAsync();Assert.Equal(application.Id,assessment.Id);Assert.Equal("Applied",assessment.Stage);Assert.Equal("Saved",assessment.State);Assert.Null(assessment.MatchPercent);Assert.Equal("Not Assessed",assessment.Decision);Assert.Equal(w.Id,assessment.Candidate!.WorkspaceId);Assert.NotNull(assessment.Candidate.PersonId);Assert.Equal("candidate@example.test",assessment.Candidate.Email);Assert.Equal("Lahore",assessment.Candidate.City);Assert.Equal(assessment.DocumentId,assessment.Candidate.DocumentId);Assert.Equal(pdf,(await db.Documents.SingleAsync()).Content);Assert.Empty(await db.Users.Where(u=>u.Email=="candidate@example.test").ToListAsync());}
        var duplicate=await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ApplyPublic(w.Id,position.Id,"Candidate Name","candidate@example.test","+92 300 1234567","Lahore","candidate.pdf",pdf));Assert.Contains("already been submitted",duplicate.Message,StringComparison.OrdinalIgnoreCase);
        await using(var db=f.Factory.CreateDbContext()){var job=await db.Positions.SingleAsync();job.CareersPublished=false;await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ApplyPublic(w.Id,position.Id,"Another Candidate","another@example.test","+92 300 9999999","Karachi","candidate.pdf",pdf));
    }
    [Fact]public async Task PublicApplicationQueuesProfileExtractionWithoutWaitingForAssessment(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("first");var w=(await owner.Overview()).Workspace;w.CareersPublished=true;await owner.SaveCompany(w);var position=new Position{WorkspaceId=w.Id,Reference="APPLY-02",Title="Public Role",Client="Company",CareersPublished=true,PublicJobDescription="Reviewed JD"};await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(position);await db.SaveChangesAsync();}
        var state=new State("first","Employer");var queue=new CvQueueSpy();var intake=new EmployerService(state,new Access(state,f.Factory),f.Factory,null,queue);var result=await intake.ApplyPublic(w.Id,position.Id,"Form Name","form@example.test","+92 300 1234567","Karachi","candidate.pdf","%PDF-1.7\nCV"u8.ToArray());Assert.True(result.ProfilePending);
        await using var verify=f.Factory.CreateDbContext();var candidate=await verify.Candidates.SingleAsync();var application=await verify.Assessments.SingleAsync();Assert.Equal([candidate.Id],queue.CandidateIds);Assert.Equal("Form Name",candidate.Name);Assert.Equal("form@example.test",candidate.Email);Assert.Equal("Karachi",candidate.City);Assert.Equal("Pending",candidate.CvProfileStatus);Assert.Null(application.MatchPercent);Assert.Equal("Not Assessed",application.Decision);Assert.Equal("Applied",application.Stage);
    }
    [Fact]public async Task TeamAcceptanceUsesSeatsWhileInvitationsDoNotAndDeactivationPreservesHistory(){
        await using var f=new Fixture();await f.Init();var team=f.Teams();var first=await team.Invite("Recruiter","recruiter@example.test");var second=await team.Invite("Second","recruiter2@example.test");Assert.Empty((await team.Team()).Members);Assert.Equal(2,(await team.Team()).Invitations.Count);Assert.Empty(f.Mail.Messages);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await team.Accept(Token(first),await Recruiter("recruiter3")));
        await team.Accept(Token(first),await Recruiter());var member=Assert.Single((await team.Team()).Members);Assert.False(member.CanCreateClients);Assert.False(member.CanCreatePositions);Assert.False(await f.RecruiterAccess().Can("Talent.View"));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await team.Accept(Token(second),await Recruiter("recruiter2")));
        await team.Update(member with{Status="Inactive"});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.RecruiterAccess().Require());
        await team.Accept(Token(second),await Recruiter("recruiter2"));var inactive=(await team.Team()).Members.Single(m=>m.Id=="recruiter");await Assert.ThrowsAsync<InvalidOperationException>(()=>team.Update(inactive with{Status="Active"}));
        Assert.Equal(2,(await team.Team()).Members.Count);Assert.Equal(2,(await team.Team()).Invitations.Count(i=>i.Status=="Accepted"));
    }
    [Fact]public async Task InvalidExpiredCancelledAndCrossWorkspaceInvitationsCannotGrantMembership(){
        await using var f=new Fixture();await f.Init();var team=f.Teams();Assert.Null(await team.Read(null));Assert.Null(await team.Read(""));var invitation=await team.Invite("Recruiter","recruiter@example.test");var id=Assert.Single((await team.Team()).Invitations).Id;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Teams("second").Cancel(id));await team.Cancel(id);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await team.Accept(Token(invitation),await Recruiter()));
        var fresh=await team.Invite("Recruiter","recruiter@example.test");Assert.NotEqual(invitation.Link,fresh.Link);Assert.Null(await team.Read(Token(invitation)));
        await using(var db=f.Factory.CreateDbContext()){(await db.EmployerInvitations.SingleAsync()).ExpiresUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();}
        Assert.Equal("Expired",(await team.Read(Token(fresh)))!.Status);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await team.Accept(Token(fresh),await Recruiter()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>team.EmailInvitation(id));Assert.Empty(f.Mail.Messages);
    }
    [Fact]public async Task EmployerPermissionsCreateScopedClientsAndPositionsAndEnforceSharedSlots(){
        await using var f=new Fixture();await f.Init();var team=f.Teams();await team.Accept(Token(await team.Invite("Recruiter","recruiter@example.test")),await Recruiter());var access=f.RecruiterAccess();Assert.False(await access.Can("Clients.Create"));
        var member=Assert.Single((await team.Team()).Members);await team.Update(member with{CanCreateClients=true,CanCreatePositions=true});var workspaceService=new WorkspaceService(f.Factory,access);var client=new Client{Name="Shared Name"};await workspaceService.SaveClient(client);var workspace=(await team.Team()).Workspace;
        Assert.Equal(workspace.Id,client.WorkspaceId);var ats=new AtsService(f.Factory,access);
        for(int i=0;i<3;i++)await ats.SavePosition(new Position{Title="Role "+i,Client=client.Name,ClientId=client.Id,Criteria=[new(){Name="Experience",Weight=100}]});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(new Position{Title="Extra",Client=client.Name,ClientId=client.Id,Criteria=[new(){Name="Experience",Weight=100}]}));
        var closed=(await ats.Positions()).First();closed.Status="Closed";closed.ClosureReason="Test Closure";await ats.SavePosition(closed);await ats.SavePosition(new Position{Title="Replacement",Client=client.Name,ClientId=client.Id,Criteria=[new(){Name="Experience",Weight=100}]});
        closed=(await ats.Positions()).Single(p=>p.Status=="Closed");closed.Status="Active";await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(closed));
        Assert.Equal(3,(await f.Service("first").Usage()).UsedPositionSlots);
        var other=f.Teams("second");await other.Accept(Token(await other.Invite("Second","recruiter2@example.test")),await Recruiter("recruiter2"));await other.Update(Assert.Single((await other.Team()).Members) with{CanCreateClients=true});var otherClients=new WorkspaceService(f.Factory,f.RecruiterAccess("recruiter2"));var sameName=new Client{Name="Shared Name"};await otherClients.SaveClient(sameName);Assert.NotEqual(client.Id,sameName.Id);Assert.Single(await workspaceService.Clients());await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequireClient(sameName.Id));
        await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(new Position{Title="Other Workspace",Reference="PRIVATE",WorkspaceId=sameName.WorkspaceId,AssignedRecruiter="recruiter"});await db.SaveChangesAsync();}
        Assert.DoesNotContain(await ats.Positions(),p=>p.Reference=="PRIVATE");
        var enabled=(await team.Team()).Members.Single();await team.Update(enabled with{CanCreatePositions=false});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.SavePosition(new Position{Title="Denied",Client=client.Name,ClientId=client.Id,Criteria=[new(){Name="Experience",Weight=100}]}));
    }
    [Fact]public async Task TeamTalentRequiresAdminGrantAndDoesNotExposeOtherWorkspacesPrivateCandidates(){
        await using var f=new Fixture();await f.Init();var team=f.Teams();await team.Accept(Token(await team.Invite("Recruiter","recruiter@example.test")),await Recruiter());var access=f.RecruiterAccess();Assert.False(await access.Can("Talent.View"));var workspace=(await team.Team()).Workspace;workspace.TeamTalentPoolEnabled=true;await f.Service("admin","Admin").SetAccess(workspace);Assert.True(await access.Can("Talent.View"));
        var other=(await f.Service("second").Overview()).Workspace;Guid candidateId;
        await using(var db=f.Factory.CreateDbContext()){var c=new Candidate{Name="Other Private Candidate"};var p=new Position{Reference="PRIVATE",WorkspaceId=other.Id,Title="Other Role"};db.Candidates.Add(c);db.Positions.Add(p);db.Assessments.Add(new Assessment{CandidateId=c.Id,PositionId=p.Id,State="Saved"});db.Candidates.Add(new Candidate{Name="Admin Pool Candidate"});await db.SaveChangesAsync();candidateId=c.Id;}
        var pool=await new AtsService(f.Factory,access).TalentCandidates();Assert.Contains(pool,c=>c.Name=="Admin Pool Candidate");Assert.DoesNotContain(pool,c=>c.Id==candidateId);await Assert.ThrowsAsync<InvalidOperationException>(()=>access.RequireTalentCandidate(candidateId));
        var member=Assert.Single((await team.Team()).Members);await team.Update(member with{CanViewTalentPool=false});Assert.False(await access.Can("Talent.View"));
    }
    [Fact]public async Task EmployerManagesOwnClientsAndAssignmentsWithoutSeeingOtherWorkspaces(){
        await using var f=new Fixture();await f.Init();var access=new Access(new State("first","Employer"),f.Factory);var otherAccess=new Access(new State("second","Employer"),f.Factory);var workspace=new WorkspaceService(f.Factory,access);var ats=new AtsService(f.Factory,access);var client=new Client{Name="Employer Client",Contacts=[new(){Name="Hiring Contact",Email="contact@example.test"}]};await workspace.SaveClient(client);var otherClient=new Client{Name="Other Client"};await new WorkspaceService(f.Factory,otherAccess).SaveClient(otherClient);
        Assert.True(await access.Can("Clients.Create"));Assert.Single(await workspace.Clients());await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequireClient(otherClient.Id));
        var team=f.Teams();await team.Accept(Token(await team.Invite("Recruiter","recruiter@example.test")),await Recruiter());var position=new Position{Title="Employer Position",Client=client.Name,ClientId=client.Id,AssignedRecruiter="first",HiringContact="Hiring Contact",Criteria=[new(){Name="Experience",Weight=100}]};await ats.SavePosition(position);Assert.Equal(client.WorkspaceId,position.WorkspaceId);Assert.Equal("first",position.AssignedRecruiter);
        var choices=await workspace.Recruiters();Assert.Contains(choices,c=>c.Username=="first");Assert.Contains(choices,c=>c.Username=="recruiter");Assert.DoesNotContain(choices,c=>c.Username=="second");
        var edit=(await ats.Positions()).Single();edit.AssignedRecruiter="second";await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(edit));edit.AssignedRecruiter="recruiter";await ats.SavePosition(edit);Assert.Single(await new AtsService(f.Factory,f.RecruiterAccess()).Positions());edit=(await ats.Positions()).Single();edit.AssignedRecruiter="first";await ats.SavePosition(edit);Assert.Empty(await new AtsService(f.Factory,f.RecruiterAccess()).Positions());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>otherAccess.RequirePosition(position.Id));Assert.Empty(await new AtsService(f.Factory,otherAccess).Positions());
        await using var db=f.Factory.CreateDbContext();Assert.Contains(await db.Audit.ToListAsync(),a=>a.Action=="Position saved"&&a.PreviousJson.Contains("recruiter"));
    }
    [Fact]public async Task AdminCreatesEmployerWorkUnderTheSelectedClientAndUsesThatTeamsAssignments(){
        await using var f=new Fixture();await f.Init();var ownerAccess=new Access(new State("first","Employer"),f.Factory);var client=new Client{Name="Employer Client"};await new WorkspaceService(f.Factory,ownerAccess).SaveClient(client);var team=f.Teams();await team.Accept(Token(await team.Invite("Recruiter","recruiter@example.test")),await Recruiter());
        var adminAccess=new Access(new State("admin","Admin"),f.Factory);var position=new Position{ClientId=client.Id,Client=client.Name,Title="Admin Created Team Role",AssignedRecruiter="recruiter",Criteria=[new(){Name="Experience",Weight=100}]};await new AtsService(f.Factory,adminAccess).SavePosition(position);Assert.Equal(client.WorkspaceId,position.WorkspaceId);Assert.Single(await new AtsService(f.Factory,ownerAccess).Positions());
        var choices=await new WorkspaceService(f.Factory,adminAccess).Recruiters(client.WorkspaceId);Assert.Contains(choices,c=>c.Username=="first");Assert.Contains(choices,c=>c.Username=="recruiter");Assert.DoesNotContain(choices,c=>c.Username=="second");
    }
    [Fact]public async Task EmployerCanRemoveUnusedClientsButArchivesClientsWithRecruitmentHistory(){
        await using var f=new Fixture();await f.Init();var access=new Access(new State("first","Employer"),f.Factory);var workspace=new WorkspaceService(f.Factory,access);var unused=new Client{Name="Unused Client",Contacts=[new(){Name="Contact"}]};await workspace.SaveClient(unused);await workspace.RemoveUnusedClient(unused.Id,unused.Revision);Assert.Empty(await workspace.Clients());
        var linked=new Client{Name="Linked Client"};await workspace.SaveClient(linked);await new AtsService(f.Factory,access).SavePosition(new Position{Title="History",Client=linked.Name,ClientId=linked.Id,Status="Draft",Criteria=[new(){Name="Experience",Weight=100}]});await Assert.ThrowsAsync<InvalidOperationException>(()=>workspace.RemoveUnusedClient(linked.Id,linked.Revision));linked=(await workspace.Clients()).Single();linked.Status="Archived";await workspace.SaveClient(linked);Assert.Equal("Archived",(await workspace.Clients()).Single().Status);Assert.Single(await new AtsService(f.Factory,access).Positions());
        var other=new Client{Name="Other Workspace"};var otherAccess=new Access(new State("second","Employer"),f.Factory);await new WorkspaceService(f.Factory,otherAccess).SaveClient(other);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>workspace.RemoveUnusedClient(other.Id,other.Revision));
    }
    [Fact]public async Task DraftPositionsDoNotUseSlotsButActivationAndReopeningRespectTheSharedLimit(){
        await using var f=new Fixture();await f.Init();var access=new Access(new State("first","Employer"),f.Factory);var ats=new AtsService(f.Factory,access);var workspace=new WorkspaceService(f.Factory,access);var client=new Client{Name="Slot Client"};await workspace.SaveClient(client);
        for(int i=0;i<4;i++)await ats.SavePosition(new Position{Title="Draft "+i,Client=client.Name,ClientId=client.Id,Status="Draft",Criteria=[new(){Name="Experience",Weight=100}]});Assert.Equal(0,(await ats.Allowance())!.Used);
        foreach(var p in (await ats.Positions()).Take(3)){p.Status="Active";await ats.SavePosition(p);}Assert.Equal(3,(await ats.Allowance())!.Used);
        var extra=(await ats.Positions()).Single(p=>p.Status=="Draft");extra.Status="Active";await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(extra));var filled=(await ats.Positions()).First(p=>p.Status=="Active");filled.Status="Filled";filled.FilledSource=Choices.FilledSources.First(s=>s!="Osynix Recruiter");await ats.SavePosition(filled);Assert.Equal(2,(await ats.Allowance())!.Used);await ats.SavePosition(extra);Assert.Equal(3,(await ats.Allowance())!.Used);
        filled=(await ats.Positions()).Single(p=>p.Status=="Filled");filled.Status="Active";await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(filled));
    }
    [Fact]public async Task WorkspacesAreIsolatedAndCompanyEditingCannotGrantPaidAccess()
    {
        await using var f=new Fixture();await f.Init();var first=f.Service("first");var second=f.Service("second");
        var a=await first.Overview();var b=await second.Overview();Assert.NotEqual(a.Workspace.Id,b.Workspace.Id);Assert.Equal(0,a.Workspace.FreeRecruiterSeats);Assert.Equal(1,a.Workspace.AdditionalRecruiterSeats);Assert.Equal(3,a.Workspace.FreePositionSlots);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>second.SaveCompany(a.Workspace));
        a.Workspace.CompanyName="First Company";a.Workspace.AdditionalPositionSlots=500;a.Workspace.TeamTalentPoolEnabled=true;await first.SaveCompany(a.Workspace);
        var saved=(await first.Overview()).Workspace;Assert.Equal("First Company",saved.CompanyName);Assert.Equal(0,saved.AdditionalPositionSlots);Assert.False(saved.TeamTalentPoolEnabled);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>first.SetAccess(saved));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>first.Administration());
        await first.Request("Position Slots",2,"Growth");Assert.Single((await first.Overview()).Requests);Assert.Empty((await second.Overview()).Requests);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("recruiter","Recruiter").Overview());
    }
    [Fact]public async Task AdminGrantsOnceAndCanRevokeWithoutDeletingRequests()
    {
        await using var f=new Fixture();await f.Init();var first=f.Service("first");var admin=f.Service("admin","Admin");await first.Request("Position Slots",2,"Growth");
        var request=Assert.Single((await first.Overview()).Requests);request.Status="Granted";request.AdminResponse="Access Approved";await admin.Respond(request);
        Assert.Equal(2,(await first.Overview()).Workspace.AdditionalPositionSlots);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>admin.Respond(request));
        request=Assert.Single((await first.Overview()).Requests);await admin.Respond(request);Assert.Equal(2,(await first.Overview()).Workspace.AdditionalPositionSlots);
        await first.Request("Team Talent Pool",99,"");request=(await first.Overview()).Requests.Single(r=>r.Feature=="Team Talent Pool");Assert.Equal(1,request.Quantity);request.Status="Granted";request.AdminResponse="Team Access Approved";await admin.Respond(request);Assert.True((await first.Overview()).Workspace.TeamTalentPoolEnabled);
        var workspace=(await first.Overview()).Workspace;workspace.TeamTalentPoolEnabled=false;workspace.AdditionalPositionSlots=0;await admin.SetAccess(workspace);var revoked=await first.Overview();Assert.False(revoked.Workspace.TeamTalentPoolEnabled);Assert.Equal(0,revoked.Workspace.AdditionalPositionSlots);Assert.Equal(2,revoked.Requests.Count);
    }
    [Fact]public async Task PrivateCompanyIsUnpublishedAndInactiveOwnersCannotExposeCareersPage()
    {
        await using var f=new Fixture();await f.Init();var service=f.Service("first");var workspace=(await service.Overview()).Workspace;
        Assert.Null(await service.PublicCompany(workspace.Id));workspace.CareersPublished=true;workspace.CompanyName="Public Company";await service.SaveCompany(workspace);Assert.NotNull(await service.PublicCompany(workspace.Id));
        await using var db=f.Factory.CreateDbContext();(await db.Users.SingleAsync(u=>u.Id=="first")).Status="Inactive";await db.SaveChangesAsync();Assert.Null(await service.PublicCompany(workspace.Id));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.Overview());
    }
    [Fact]public async Task InvalidLogosWebsitesAndStaleCompanyEditsAreRejected()
    {
        await using var f=new Fixture();await f.Init();var service=f.Service("first");var workspace=(await service.Overview()).Workspace;
        workspace.Website="javascript:alert(1)";await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveCompany(workspace));workspace.Website="";workspace.LogoContent="<svg onload='alert(1)'>"u8.ToArray();await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveCompany(workspace));workspace.LogoContent=null;workspace.CompanyName="Changed Company";
        await service.SaveCompany(workspace);await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>service.SaveCompany(workspace));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.Request("Position Slots",-1,""));
    }
}
