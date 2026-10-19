using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class SupportInboxTests
{
    sealed class State(string id,string role):AuthenticationStateProvider
    {
        public ClaimsPrincipal Principal=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id),new Claim(ClaimTypes.Role,role)},"Test"));
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(Principal));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Fixture:IAsyncDisposable
    {
        readonly SqliteConnection connection=new("Data Source=:memory:");
        public Factory Factory=null!;
        public async Task Init(){await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);await using var db=Factory.CreateDbContext();await db.Database.EnsureCreatedAsync();
            foreach(var role in new[]{"Admin","Recruiter"})db.Roles.Add(new IdentityRole(role){Id=role});
            foreach(var id in new[]{"admin","user","other"}){var role=id=="admin"?"Admin":"Recruiter";db.Users.Add(new(){Id=id,FullName=id,UserName=id,Email=id+"@example.test",EmailConfirmed=true,AccountType=id=="admin"?"Admin":AccountTypes.Freelance});db.UserRoles.Add(new(){UserId=id,RoleId=role});}await db.SaveChangesAsync();}
        public SupportInboxService Service(string id){var state=new State(id,id=="admin"?"Admin":"Recruiter");return new(state,Factory,new(state,Factory),new());}
        public ClaimsPrincipal Principal(string id)=>new State(id,id=="admin"?"Admin":"Recruiter").Principal;
        public ValueTask DisposeAsync()=>connection.DisposeAsync();
    }

    [Fact]
    public async Task SessionsGroupByUserArchiveForAdminAndExportRemainsPrivate()
    {
        await using var f=new Fixture();await f.Init();var user=f.Service("user");var admin=f.Service("admin");
        var technical=await user.StartGeneral("Technical Issue","CV upload");await user.Send(technical,"Unable to upload my CV");
        var billing=await admin.StartForUser("user","Payment review","Subscription & Payment");await admin.Send(billing,"Please send payment proof");
        var group=Assert.Single(SupportInboxService.GroupInbox(await admin.Inbox()));Assert.Equal("user",group.UserId);Assert.Equal(2,group.Sessions);Assert.True(group.Unread>0);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other").Open(technical));
        await user.EndSession(technical);Assert.DoesNotContain(await user.Inbox(),x=>x.Id==technical);Assert.Contains(await admin.Inbox(),x=>x.Id==technical&&x.Status=="Closed"&&x.ClosedUtc!=null);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>user.Send(technical,"New issue"));
        var next=await user.StartGeneral("Technical Issue","CV upload");Assert.NotEqual(technical,next);
        await using var db=f.Factory.CreateDbContext();
        var transcript=await SupportInboxService.Transcript(db,f.Principal("user"),technical);Assert.NotNull(transcript);Assert.Contains("Unable to upload",System.Text.Encoding.UTF8.GetString(transcript.Value.Content));Assert.Contains("Technical Issue",System.Text.Encoding.UTF8.GetString(transcript.Value.Content));Assert.Contains("Closed",System.Text.Encoding.UTF8.GetString(transcript.Value.Content));
        Assert.Null(await SupportInboxService.Transcript(db,f.Principal("other"),technical));Assert.NotNull(await SupportInboxService.Transcript(db,f.Principal("admin"),technical));
        Assert.Equal(3,(await admin.Inbox()).Count);
    }

    [Fact]
    public async Task RequestReviewReceivesNewUserEvidenceWithoutReadingEntireChat()
    {
        await using var f=new Fixture();await f.Init();Guid requestId,sessionId,workspaceId;
        await using(var db=f.Factory.CreateDbContext()){
            var w=new EmployerWorkspace{OwnerId="user",CompanyName="Freelancer",Kind=AccountTypes.Freelance};db.Add(w);workspaceId=w.Id;
            var request=new EmployerAccessRequest{WorkspaceId=w.Id,Feature="Subscription: Starter",Status="Needs Information",AdminResponse="Send payment proof",RespondedUtc=DateTime.UtcNow.AddMinutes(-2)};db.Add(request);requestId=request.Id;await db.SaveChangesAsync();
            await SupportInboxService.RecordRequestResponse(db,request,w,"admin");await db.SaveChangesAsync();sessionId=await db.SupportConversations.Select(x=>x.Id).SingleAsync();
        }
        await f.Service("user").Send(sessionId,"Here is the receipt","receipt.png",new byte[]{137,80,78,71,13,10,26,10,0});
        var information=(await f.Service("admin").RequestInformation(workspaceId))[requestId];Assert.Equal(sessionId,information.SessionId);var reply=Assert.Single(information.Messages);Assert.Equal("Here is the receipt",reply.Body);Assert.Equal("receipt.png",reply.AttachmentName);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other").RequestInformation(workspaceId));
        await using(var db=f.Factory.CreateDbContext()){
            Assert.NotNull(await SupportInboxService.Download(db,f.Principal("admin"),reply.Id));Assert.Null(await SupportInboxService.Download(db,f.Principal("other"),reply.Id));
            var request=await db.EmployerAccessRequests.SingleAsync();request.Status="Under Review";request.RespondedUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        }
        Assert.Empty(await f.Service("admin").RequestInformation(workspaceId));
    }

    [Fact]
    public async Task DeletedChatsRetainAccountHistoryAndReturnOnNewMessages()
    {
        await using var f=new Fixture();await f.Init();Guid workspaceId,otherWorkspace;
        await using(var db=f.Factory.CreateDbContext()){
            var workspace=new EmployerWorkspace{OwnerId="user",CompanyName="User account"};var other=new EmployerWorkspace{OwnerId="other",CompanyName="Other account"};db.AddRange(workspace,other);workspaceId=workspace.Id;otherWorkspace=other.Id;
            db.Users.Add(new(){Id="admin2",FullName="Second Admin",UserName="admin2",Email="admin2@example.test",AccountType="Admin"});db.UserRoles.Add(new(){UserId="admin2",RoleId="Admin"});await db.SaveChangesAsync();
        }
        var state=new State("admin2","Admin");var secondAdmin=new SupportInboxService(state,f.Factory,new(state,f.Factory),new());
        var user=f.Service("user");var admin=f.Service("admin");var id=await user.StartGeneral("Technical Issue","Upload issue");await user.Send(id,"Please check my upload");
        await admin.DeleteChat(id);Assert.Empty(await admin.Inbox());Assert.Contains(await secondAdmin.Inbox(),c=>c.Id==id);
        Assert.Single(await admin.AccountHistory(workspaceId));var history=await admin.AccountSession(workspaceId,id);Assert.Equal("Please check my upload",Assert.Single(history.Messages).Body);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>user.AccountHistory(workspaceId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>admin.AccountSession(otherWorkspace,id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other").DeleteChat(id));
        await user.DeleteChat(id);Assert.Empty(await user.Inbox());
        await secondAdmin.Send(id,"We are reviewing the issue");Assert.Contains(await admin.Inbox(),c=>c.Id==id);Assert.Contains(await user.Inbox(),c=>c.Id==id);
        await user.EndSession(id);Assert.Equal("Closed",(await admin.AccountSession(workspaceId,id)).Status);Assert.Single(await admin.AccountHistory(workspaceId));
    }

    [Fact]
    public async Task WorkspaceViewingSubAdminCanReadAccountHistoryWithoutMessagingPermissions()
    {
        await using var f=new Fixture();await f.Init();Guid workspaceId;
        await using(var db=f.Factory.CreateDbContext()){
            db.Roles.Add(new IdentityRole("SubAdmin"){Id="SubAdmin"});db.Users.Add(new(){Id="viewer",UserName="viewer",AccountType="SubAdmin"});db.UserRoles.Add(new(){UserId="viewer",RoleId="SubAdmin"});db.UserClaims.Add(new(){UserId="viewer",ClaimType=Access.AdminPermissionClaim,ClaimValue="admin.workspaces.view"});
            var workspace=new EmployerWorkspace{OwnerId="user",CompanyName="Account"};db.Add(workspace);workspaceId=workspace.Id;await db.SaveChangesAsync();
        }
        var id=await f.Service("user").StartGeneral();await f.Service("user").Send(id,"Account history message");
        var state=new State("viewer","SubAdmin");var service=new SupportInboxService(state,f.Factory,new(state,f.Factory),new());
        Assert.Single(await service.AccountHistory(workspaceId));Assert.Single((await service.AccountSession(workspaceId,id)).Messages);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.Send(id,"Not permitted"));
        await using var check=f.Factory.CreateDbContext();Assert.NotNull(await SupportInboxService.Transcript(check,state.Principal,id));
    }
}
