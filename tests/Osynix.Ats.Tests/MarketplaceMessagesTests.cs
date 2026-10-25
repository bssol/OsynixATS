using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class MarketplaceMessagesTests
{
    sealed class State(string id, string role, string? mode = null) : AuthenticationStateProvider {
        public override Task<AuthenticationState> GetAuthenticationStateAsync(){var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,id),new(ClaimTypes.Role,role)};if(mode!=null)claims.Add(new(ProfileModes.Claim,mode));return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims,"Test"))));}
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Mail:IAccountEmail{public bool Configured=>false;public Task Send(string a,string s,string b)=>throw new InvalidOperationException("No test email");}
    sealed class Fixture:IAsyncDisposable {
        readonly SqliteConnection connection=new("Data Source=:memory:");
        public Factory Factory=null!;public Guid Workspace,Position,AgencyPosition;
        public async Task Init(){
            await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);
            await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);
            foreach(var role in new[]{"Employer","Recruiter"})db.Roles.Add(new IdentityRole(role){Id=role});
            foreach(var id in new[]{"owner","other-owner","free","other-free"}){var role=id.Contains("owner")?"Employer":"Recruiter";db.Users.Add(new(){Id=id,UserName=id,FullName=id,Email=id+"@example.test",NormalizedEmail=(id+"@example.test").ToUpperInvariant(),EmailConfirmed=true,AccountType=role=="Employer"?AccountTypes.Employer:AccountTypes.Freelance});db.UserRoles.Add(new(){UserId=id,RoleId=role});}
            await db.SaveChangesAsync();
            var w=await EmployerService.EnsureWorkspace(db,await db.Users.SingleAsync(u=>u.Id=="owner"));Workspace=w.Id;
            var tracked=await db.EmployerWorkspaces.SingleAsync(x=>x.Id==w.Id);tracked.AgencyEnabled=true;
            SubscriptionRules.Apply(tracked,await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Agency"&&p.Code=="free"));
            foreach(var id in new[]{"free","other-free"})await SubscriptionTestPlans.Marketplace(db,id);
            foreach(var id in new[]{"free","other-free"})db.FreelancerProfiles.Add(new(){UserId=id,Published=true,Headline="Recruiter",Skills="Sourcing",Bio="Recruitment expert"});
            var p=new Position{Reference="A1",Title="Engineer",Client="Employer",WorkspaceId=w.Id,Status="Active"};db.Add(p);Position=p.Id;
            var a=new Position{Reference="A2",Title="Agency Engineer",Client="Client",WorkspaceId=w.Id,Status="Active",IsAgencyPosition=true};db.Add(a);AgencyPosition=a.Id;
            await db.SaveChangesAsync();
        }
        public MarketplaceMessages Service(string id,string? mode=null)=>new(new State(id,id.Contains("owner")?"Employer":"Recruiter",mode),Factory,new(),new Mail(),new ConfigurationBuilder().Build());
        public ProjectInviteDraft Draft(Guid? position=null)=>new(position??Position,"Project","Source and shortlist candidates","Hello","To be agreed","Short-term project",true);
        public async Task<ProjectInvitation> Invitation(Guid conversation){await using var db=Factory.CreateDbContext();return await db.ProjectInvitations.AsNoTracking().SingleAsync(i=>i.ConversationId==conversation);}
        public ValueTask DisposeAsync()=>connection.DisposeAsync();
    }

    [Fact] public async Task EmployerOutreachIsChargedOnContactAndExistingChatSurvivesDowngrade(){
        await using var f=new Fixture();await f.Init();
        await using(var db=f.Factory.CreateDbContext()){
            var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==f.Workspace);workspace.AgencyEnabled=false;
            SubscriptionRules.Apply(workspace,await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Employer"&&p.Code=="starter"));await db.SaveChangesAsync();
        }
        var owner=f.Service("owner");var conversation=await owner.Start("free");
        await using(var db=f.Factory.CreateDbContext())Assert.Equal(0,await SubscriptionRules.FreelancerApproaches(db,f.Workspace));
        await owner.Send(conversation,"Can we discuss an assignment?");await owner.Send(conversation,"Here are more details.");
        await owner.Invite("free",f.Draft());
        await using(var db=f.Factory.CreateDbContext()){
            Assert.Equal(1,await SubscriptionRules.FreelancerApproaches(db,f.Workspace));
            var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==f.Workspace);
            SubscriptionRules.Apply(workspace,await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Employer"&&p.Code=="free"));await db.SaveChangesAsync();
        }
        await owner.Send(conversation,"Continuing our existing discussion.");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>owner.Start("other-free"));
        Assert.Contains((await owner.Open(conversation)).Messages,m=>m.Body=="Continuing our existing discussion.");
    }

    [Fact] public async Task InboxDeletionIsParticipantOnlyAndKeepsInvitationsAndOtherCopy(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("owner");var freelancer=f.Service("free");var id=await owner.Invite("free",f.Draft());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other-free").DeleteChat(id));
        await owner.DeleteChat(id);Assert.Empty(await owner.Inbox());Assert.Single(await freelancer.Inbox());
        var invitation=await f.Invitation(id);Assert.Equal("Pending",invitation.Status);
        await freelancer.Send(id,"What is the start date?");Assert.Contains(await owner.Inbox(),c=>c.Id==id);
        await freelancer.Respond(invitation.Id,invitation.Revision,"Accepted");Assert.Equal("Accepted",(await f.Invitation(id)).Status);
    }

    [Fact] public async Task InvitationAppearsInRecipientInboxAndAcceptAssignsOnlyEmployerPositionWithoutInternalSeats(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("owner");var free=f.Service("free");var id=await owner.Invite("free",f.Draft());
        var incoming=Assert.Single(await free.Inbox());Assert.Equal(1,incoming.Unread);Assert.Equal(id,incoming.Id);
        var view=await free.Open(id);Assert.False(view.Employer);Assert.Equal(0,await free.Unread());Assert.Equal("Hello",Assert.Single(view.Messages).Body);
        var invite=await f.Invitation(id);Assert.Contains("Messages",invite.EmailDelivery);
        await free.Respond(invite.Id,invite.Revision,"Accepted");
        await using var db=f.Factory.CreateDbContext();Assert.Equal(f.Workspace,(await db.WorkspaceMemberships.SingleAsync()).WorkspaceId);Assert.Equal(f.Position,(await db.PositionRecruiterAssignments.SingleAsync()).PositionId);
        Assert.Equal(1,(await db.EmployerWorkspaces.SingleAsync(w=>w.Id==f.Workspace)).FreeRecruiterSeats);
        Assert.Equal("Accepted",(await f.Invitation(id)).Status);Assert.True(await owner.Unread()>0);
        await Assert.ThrowsAnyAsync<Exception>(()=>free.Respond(invite.Id,invite.Revision,"Accepted"));
    }

    [Fact] public async Task ConversationMessagesAndAttachmentsArePrivateToParticipants(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("owner");var id=await owner.Invite("free",f.Draft());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other-free").Open(id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other-owner").Send(id,"Attempted access"));
        Assert.Empty(await f.Service("other-owner").Inbox());Assert.Empty(await f.Service("other-free").Inbox());
        await owner.Send(id,"Role brief","role.pdf","%PDF-test"u8.ToArray());
        var message=(await f.Service("free").Open(id)).Messages.Last();
        await using var db=f.Factory.CreateDbContext();var actual=(await new State("free","Recruiter").GetAuthenticationStateAsync()).User;var unrelated=(await new State("other-free","Recruiter").GetAuthenticationStateAsync()).User;
        Assert.NotNull(await MarketplaceMessages.Download(db,actual,message.Id));Assert.Null(await MarketplaceMessages.Download(db,unrelated,message.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.Send(id,"Bad file","evil.exe",new byte[]{1,2,3}));
    }

    [Fact] public async Task QuestionKeepsInvitationPendingAndDeclineDoesNotGrantWorkspaceAccess(){
        await using var f=new Fixture();await f.Init();var id=await f.Service("owner").Invite("free",f.Draft());var free=f.Service("free");var invite=await f.Invitation(id);
        await free.Respond(invite.Id,invite.Revision,"Question","What is the timeline?");Assert.Equal("Pending",(await f.Invitation(id)).Status);
        invite=await f.Invitation(id);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("owner").Respond(invite.Id,invite.Revision,"Accepted"));
        await free.Respond(invite.Id,invite.Revision,"Declined");await using var db=f.Factory.CreateDbContext();Assert.Empty(await db.WorkspaceMemberships.ToListAsync());Assert.Empty(await db.PositionRecruiterAssignments.ToListAsync());
    }

    [Fact] public async Task DuplicatePendingAndForeignOrWrongModePositionsAreRejectedAndClosedPositionCannotBeAccepted(){
        await using var f=new Fixture();await f.Init();var owner=f.Service("owner");var id=await owner.Invite("free",f.Draft());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.Invite("free",f.Draft()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.Invite("free",f.Draft(f.AgencyPosition)));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.Invite("free",f.Draft(Guid.NewGuid())));
        var agency=await f.Service("owner","Agency").Invite("free",f.Draft(f.AgencyPosition));Assert.NotEqual(id,agency);
        await using(var db=f.Factory.CreateDbContext()){(await db.Positions.SingleAsync(p=>p.Id==f.Position)).Status="Closed";await db.SaveChangesAsync();}
        var invite=await f.Invitation(id);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").Respond(invite.Id,invite.Revision,"Accepted"));
    }

    [Fact] public async Task ExistingExternalMemberCanReceiveMoreAssignmentsButInvitationCannotRestoreRevokedAccess(){
        await using var f=new Fixture();await f.Init();await using(var db=f.Factory.CreateDbContext()){db.WorkspaceMemberships.Add(new(){WorkspaceId=f.Workspace,UserId="free"});await db.SaveChangesAsync();}
        var id=await f.Service("owner").Invite("free",f.Draft());var invite=await f.Invitation(id);await f.Service("free").Respond(invite.Id,invite.Revision,"Accepted");
        await using(var db=f.Factory.CreateDbContext()){Assert.Equal(1,await db.WorkspaceMemberships.CountAsync());(await db.WorkspaceMemberships.SingleAsync()).Status="Inactive";await db.SaveChangesAsync();}
        var agency=await f.Service("owner","Agency").Invite("free",f.Draft(f.AgencyPosition));invite=await f.Invitation(agency);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").Respond(invite.Id,invite.Revision,"Accepted"));
    }
}
