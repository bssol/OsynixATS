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

public class EmailTemplateTests
{
    sealed class State(string user,string role):AuthenticationStateProvider{
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user),new Claim(ClaimTypes.Role,role)},"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Mail:IAccountEmail{
        public bool Configured=>true;public string Subject="",Body="";public bool Html;
        public Task Send(string email,string subject,string body){Subject=subject;Body=body;Html=false;return Task.CompletedTask;}
        public Task SendHtml(string email,string subject,string body){Subject=subject;Body=body;Html=true;return Task.CompletedTask;}
    }
    sealed class Fixture:IAsyncDisposable{
        readonly SqliteConnection connection=new("Data Source=:memory:");public Factory Factory=null!;public Guid First,Second;
        public async Task Init(){await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);await using var db=Factory.CreateDbContext();await db.Database.EnsureCreatedAsync();foreach(var role in new[]{"Admin","Employer"})db.Roles.Add(new IdentityRole(role){Id=role});foreach(var id in new[]{"admin","a","b"}){var role=id=="admin"?"Admin":"Employer";db.Users.Add(new(){Id=id,UserName=id,FullName=id,EmailConfirmed=true,AccountType=role});db.UserRoles.Add(new(){UserId=id,RoleId=role});}var a=new EmployerWorkspace{OwnerId="a",CompanyName="A"};var b=new EmployerWorkspace{OwnerId="b",CompanyName="B"};db.EmployerWorkspaces.AddRange(a,b);await db.SaveChangesAsync();First=a.Id;Second=b.Id;}
        public EmailTemplateService Service(string id){var state=new State(id,id=="admin"?"Admin":"Employer");return new(Factory,state,new Access(state,Factory));}
        public async Task Grant(Guid workspace,bool value){await using var db=Factory.CreateDbContext();var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==workspace);w.EmailTemplatesEnabled=value;await db.SaveChangesAsync();}
        public ValueTask DisposeAsync()=>connection.DisposeAsync();
    }
    [Fact]public async Task GrantAndRevocationProtectListingAndSaving(){
        await using var f=new Fixture();await f.Init();var service=f.Service("a");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.List());
        await f.Grant(f.First,true);var draft=(await service.List()).First(t=>t.Key=="application-received");draft.Name="My Receipt";await service.Save(draft);
        Assert.Contains(await service.List(),t=>t.Name=="My Receipt");
        await f.Grant(f.First,false);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.List());await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.Save(draft));
    }
    [Fact]public async Task WorkspaceOverridesAndCustomTemplatesStayIsolatedAndRejectStaleUpdates(){
        await using var f=new Fixture();await f.Init();await f.Grant(f.First,true);await f.Grant(f.Second,true);
        var a=f.Service("a");var original=(await a.List()).First(t=>t.Key=="application-received");original.WorkspaceId=f.Second;original.Subject="Private A Subject";await a.Save(original);
        Assert.Equal("Private A Subject",(await a.List()).First(t=>t.Key==original.Key).Subject);Assert.NotEqual("Private A Subject",(await f.Service("b").List()).First(t=>t.Key==original.Key).Subject);
        var stale=(await a.List()).First(t=>t.Key==original.Key);var fresh=(await a.List()).First(t=>t.Key==original.Key);fresh.Subject="Changed";await a.Save(fresh);await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>a.Save(stale));
        await a.Save(new(){Name="Private Custom",Subject="Hello {{CandidateName}}",BodyHtml="<p>Private Message</p>"});Assert.Contains(await a.List(),t=>t.Name=="Private Custom");Assert.DoesNotContain(await f.Service("b").List(),t=>t.Name=="Private Custom");
    }
    [Fact]public async Task HtmlFormattingAndMergeFieldsAreSafeAndRevocationFallsBackToExistingMessage(){
        await using var f=new Fixture();await f.Init();await f.Grant(f.First,true);var s=f.Service("a");var template=(await s.List()).First(t=>t.Key=="application-received");template.BodyHtml="<p><strong>Hello {{CandidateName}}</strong><script>alert(1)</script><a href='javascript:alert(1)' onclick='bad()'>Link</a></p>";await s.Save(template);
        var mail=new Mail();await EmailTemplateService.Send(f.Factory,mail,f.First,template.Key,"test@example.test","Fallback","Original",new(){["PositionTitle"]="Developer",["CandidateName"]="<img src=x onerror=bad()>"});
        Assert.True(mail.Html);Assert.Contains("<strong>",mail.Body);Assert.Contains("&lt;img",mail.Body);Assert.DoesNotContain("<script",mail.Body);Assert.DoesNotContain("javascript:",mail.Body);Assert.DoesNotContain("onclick",mail.Body);
        await f.Grant(f.First,false);await EmailTemplateService.Send(f.Factory,mail,f.First,template.Key,"test@example.test","Fallback","Original",new());Assert.False(mail.Html);Assert.Equal("Original",mail.Body);
    }
    [Fact]public async Task RequiredLinksAndSecurityTemplatesAreProtected(){
        await using var f=new Fixture();await f.Init();await f.Grant(f.First,true);var s=f.Service("a");var t=(await s.List()).First(t=>t.Key=="profiles-review");t.BodyHtml="<p>Missing Link</p>";await Assert.ThrowsAsync<InvalidOperationException>(()=>s.Save(t));Assert.DoesNotContain(await s.List(),t=>t.Recipient=="System");
        var security=(await f.Service("admin").List()).First(t=>t.Key=="email-verification");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>s.Save(security));security.BodyHtml="<p>Your Code: <strong>{{VerificationCode}}</strong></p>";await f.Service("admin").Save(security);
        var mail=new Mail();await EmailTemplateService.Send(f.Factory,mail,null,security.Key,"test@example.test","Code","Fallback",new(){["VerificationCode"]="123456"});Assert.True(mail.Html);Assert.Contains("123456",mail.Body);
    }
    [Fact]public async Task AdminCheckboxPersistsAndApprovalGrantsOnlyEmailTemplates(){
        await using var f=new Fixture();await f.Init();var state=new State("admin","Admin");var admin=new EmployerService(state,new Access(state,f.Factory),f.Factory);
        var account=(await admin.Administration()).Single(a=>a.Workspace.Id==f.First);account.Workspace.EmailTemplatesEnabled=true;await admin.SetAccess(account.Workspace);
        Assert.True((await admin.Administration()).Single(a=>a.Workspace.Id==f.First).Workspace.EmailTemplatesEnabled);
        Assert.NotEmpty(await f.Service("a").List());
        var ownerState=new State("b","Employer");var owner=new EmployerService(ownerState,new Access(ownerState,f.Factory),f.Factory);await owner.Request("Email Templates",1,"Enable Templates");
        var request=(await admin.Administration()).Single(a=>a.Workspace.Id==f.Second).Requests.Single();request.Status="Granted";request.AdminResponse="Access Granted";await admin.Respond(request);
        await using var db=f.Factory.CreateDbContext();var granted=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==f.Second);Assert.True(granted.EmailTemplatesEnabled);Assert.False(granted.MasterTalentPoolEnabled);Assert.False(granted.AgencyEnabled);
    }
}
