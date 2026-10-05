using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Osynix.Ats.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Xunit;
namespace Osynix.Ats.Tests;
public class HttpWorkflowTests
{
    sealed class Host:WebApplicationFactory<AtsUser> {
        readonly string path=Path.Combine(Path.GetTempPath(),$"osynix-http-{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Development");builder.ConfigureLogging(x=>{x.ClearProviders();x.AddFilter(_=>false);});builder.ConfigureServices(services=>{
            services.RemoveAll<IDbContextFactory<AtsDbContext>>();services.RemoveAll<DbContextOptions<AtsDbContext>>();services.RemoveAll<AtsDbContext>();services.AddDbContextFactory<AtsDbContext>(o=>o.UseSqlite($"Data Source={path}"));services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });}
        public override async ValueTask DisposeAsync(){await base.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
    }
    static async Task<HttpClient> Login(Host host,string role="Admin") {
        var client=host.CreateClient(new(){AllowAutoRedirect=false});using(var scope=host.Services.CreateScope()){var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var user=new AtsUser{UserName="HttpUser",Email="http@example.test",EmailConfirmed=true};Assert.True((await manager.CreateAsync(user,"Test-password!123")).Succeeded);Assert.True((await manager.AddToRoleAsync(user,role)).Succeeded);}
        var html=await client.GetStringAsync("/Account/Login");var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var result=await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","HttpUser"},{"Password","Test-password!123"},{"__RequestVerificationToken",token}}));Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);return client;
    }
    [Fact] public async Task AuthenticatedWorkspacesAndHistoricalReportsRenderStoredEvidence()
    {
        await using var host=new Host();using var client=await Login(host);Guid applicationId,candidateId;using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var p=new Position{Reference="001",Title="Test role",Client="Test client",Criteria=[new(){Name="Experience",Weight=100}]};var c=new Candidate{LegacyId="CAND-001",Name="HTTP Candidate",Email="secret-contact@example.test",Phone="123SECRET",Summary="Profile summary"};db.Positions.Add(p);db.Candidates.Add(c);var a=new Assessment{PositionId=p.Id,CandidateId=c.Id,State="Saved",CandidateName=c.Name,Stage="Shortlisted",Decision="Shortlist",SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"Historical summary","","",p.Client,p.Title,p.Reference))};db.Assessments.Add(a);var v=DatabaseInitializer.VersionOf(a);v.Decision="Hold";v.MatchPercent=55;db.AssessmentVersions.Add(v);await db.SaveChangesAsync();applicationId=a.Id;candidateId=c.Id;}
        foreach(var route in new[]{"/","/clients","/positions?ref=001","/assessment","/candidates","/talent","/pipeline?active=true&queue=Shortlisted","/settings","/import",$"/candidates?id={candidateId}",$"/assessment/{applicationId}"}){var response=await client.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var rendered=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("The page could not load",rendered);Assert.DoesNotContain("Application.Decision",rendered);Assert.DoesNotContain("a.Decision",rendered);Assert.DoesNotContain("disabled=\"Busy\"",rendered);Assert.DoesNotContain("role=\"alert\">Error",rendered);}
        var report=await client.GetStringAsync($"/reports?ids={applicationId}&version=1");Assert.Contains("55%",report);Assert.Contains("Recommendation: Hold",report);Assert.Contains("no criterion results were stored",report);Assert.DoesNotContain("secret-contact",report);Assert.DoesNotContain("123SECRET",report);
        using var checkScope=host.Services.CreateScope();var check=checkScope.ServiceProvider.GetRequiredService<AtsDbContext>();Assert.Equal("Shortlist",(await check.Assessments.SingleAsync()).Decision);Assert.Contains(await check.Audit.ToListAsync(),x=>x.Action=="Report generated");
    }
    sealed class AdminState(string id):AuthenticationStateProvider {public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,"HttpUser"),new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Role,"Admin")],"Test"))));}
    [Fact] public async Task AdministratorCanActivateImportedAccountSetPasswordAndChangeGrants()
    {
        await using var host=new Host();using var client=await Login(host);string adminId,importedId;using(var scope=host.Services.CreateScope()){var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();adminId=(await manager.FindByNameAsync("HttpUser"))!.Id;var imported=new AtsUser{UserName="Imported",Email="imported@example.test",Status="Pending"};Assert.True((await manager.CreateAsync(imported)).Succeeded);Assert.True((await manager.AddToRoleAsync(imported,"Recruiter")).Succeeded);importedId=imported.Id;}
        var service=new AdminService(new Access(new AdminState(adminId)),host.Services.GetRequiredService<IServiceScopeFactory>(),host.Services.GetRequiredService<IDbContextFactory<AtsDbContext>>());var initial=(await service.Users()).Single(x=>x.Id==importedId);Assert.False(initial.HasPassword);await service.ResetPassword(importedId,"Imported-password!123");await service.UpdateUser(initial with{Status="Active",CanExportReports=false,FullName="Imported Recruiter"});var actual=(await service.Users()).Single(x=>x.Id==importedId);Assert.True(actual.HasPassword);Assert.Equal("Active",actual.Status);Assert.False(actual.CanExportReports);
        var admin=(await service.Users()).Single(x=>x.Id==adminId);await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UpdateUser(admin with{Status="Inactive"}));using var check=host.Services.CreateScope();var users=check.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(importedId))!,"Imported-password!123"));
    }
    [Fact] public async Task ReportGrantAndInactiveStatusProtectFileAndReportEndpointsImmediately()
    {
        await using var host=new Host();using var client=await Login(host,"Recruiter");Guid docId;using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var u=await db.Users.SingleAsync(x=>x.UserName=="HttpUser");u.CanExportReports=false;var doc=new StoredDocument{FileName="test.txt",Content="test"u8.ToArray()};db.Documents.Add(doc);await db.SaveChangesAsync();docId=doc.Id;}
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/reports?ids={Guid.NewGuid()}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/files/{docId}")).StatusCode);
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.Users.SingleAsync(x=>x.UserName=="HttpUser")).Status="Inactive";await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/files/{docId}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/reports?ids={Guid.NewGuid()}")).StatusCode);
    }
}
