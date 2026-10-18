using System.Net;
using System.Net.Http.Json;
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
    [Fact] public async Task AdminWorkspaceDirectoryRendersCategoriesViewsAndRealOwnerDetails(){
        await using var host=new Host();using var client=await Login(host);
        using(var scope=host.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
            var user=new AtsUser{Id="directory-owner",UserName="directory-owner",Email="owner@directory.test",FullName="Directory Owner",EmailConfirmed=true,AccountType=AccountTypes.Employer};db.Users.Add(user);
            db.UserRoles.Add(new(){UserId=user.Id,RoleId=(await db.Roles.SingleAsync(r=>r.Name=="Employer")).Id});
            db.EmployerWorkspaces.Add(new(){OwnerId=user.Id,CompanyName="Directory Employer"});await db.SaveChangesAsync();
        }
        var response=await client.GetAsync("/settings/employers");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        foreach(var text in new[]{"Employer Accounts","Agency Accounts","Freelance Recruiters","List View","Card View","Osynix.Ats.styles.css","Directory Employer","Directory Owner","owner@directory.test","Search Accounts","Accounts Per Page"})Assert.Contains(text,html);
        Assert.DoesNotContain("The operation failed",html);Assert.DoesNotContain("The page could not load",html);
    }
    [Fact]public async Task AdminTemplatePageRendersApprovedLibraryAndCreateAction(){
        await using var host=new Host();using var client=await Login(host);var html=WebUtility.HtmlDecode(await client.GetStringAsync("/settings/email-templates"));
        foreach(var text in new[]{"Select Template","Create Template","Interview Invitation","Application Received","Offer Notification","Email Verification Code","Email Templates"})Assert.Contains(text,html);
        Assert.DoesNotContain("The operation failed",html);Assert.DoesNotContain("The page could not load",html);
    }
    [Fact] public async Task CareersPageAndCoverArePublicOnlyWhileTheEmployerPublishesThem(){
        await using var host=new Host();using var signedIn=await Login(host,"Employer");Guid id;Guid jobId;var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0lcAAAAASUVORK5CYII=");
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var owner=await db.Users.SingleAsync(u=>u.UserName=="HttpUser");var w=new EmployerWorkspace{OwnerId=owner.Id,CompanyName="Public Company",CareersPublished=true,CareersCoverMode="Upload",CareersCoverContent=png,CareersCoverContentType="image/png",BusinessEmail="PRIVATE-CONTACT@example.test"};db.EmployerWorkspaces.Add(w);var job=new Position{WorkspaceId=w.Id,Reference="PUB-HTTP",Title="Public Operations Role",Client="PRIVATE CLIENT",HiringContact="PRIVATE MANAGER",CareersPublished=true,PublicJobDescription="Public Description",JobDescription="PRIVATE JD"};db.Positions.Add(job);await db.SaveChangesAsync();id=w.Id;jobId=job.Id;}
        using var anonymous=host.CreateClient();var html=await anonymous.GetStringAsync($"/careers/{id}");Assert.Contains("Find Your Next Opportunity",html);Assert.Contains("Public Operations Role",html);Assert.DoesNotContain("PRIVATE",html);var detail=await anonymous.GetStringAsync($"/careers/{id}/jobs/{jobId}");Assert.Contains("Public Description",detail);Assert.DoesNotContain("PRIVATE",detail);
        var cover=await anonymous.GetAsync($"/careers/{id}/cover");Assert.Equal(HttpStatusCode.OK,cover.StatusCode);Assert.Equal("image/png",cover.Content.Headers.ContentType!.MediaType);Assert.Equal(png,await cover.Content.ReadAsByteArrayAsync());
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.EmployerWorkspaces.SingleAsync()).CareersPublished=false;await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync($"/careers/{id}/cover")).StatusCode);Assert.DoesNotContain("Public Operations Role",await anonymous.GetStringAsync($"/careers/{id}"));
    }
    [Fact]public async Task MasterPoolIsDisabledAndEmptyUntilAnExplicitWorkspaceGrant(){await using var host=new Host();using var client=await Login(host,"Recruiter");Guid workspaceId;using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var user=await db.Users.SingleAsync(u=>u.UserName=="HttpUser");user.AccountType=AccountTypes.Freelance;await db.SaveChangesAsync();var w=await EmployerService.EnsureWorkspace(db,user);workspaceId=w.Id;var person=new Candidate{Name="SECRET MASTER PROFILE",Email="master-person@example.test"};db.Candidates.Add(person);await TalentIdentity.Attach(db,person);await db.SaveChangesAsync();}var locked=await client.GetStringAsync("/master-talent");Assert.Contains("No Results",locked);Assert.Contains("aria-disabled=\"true\"",locked);Assert.DoesNotContain("SECRET MASTER PROFILE",locked);Assert.DoesNotContain("href=\"/master-talent\"",locked);using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.EmployerWorkspaces.SingleAsync(w=>w.Id==workspaceId)).MasterTalentPoolEnabled=true;await db.SaveChangesAsync();}Assert.Contains("SECRET MASTER PROFILE",await client.GetStringAsync("/master-talent"));using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.EmployerWorkspaces.SingleAsync(w=>w.Id==workspaceId)).MasterTalentPoolEnabled=false;await db.SaveChangesAsync();}Assert.DoesNotContain("SECRET MASTER PROFILE",await client.GetStringAsync("/master-talent"));}
    [Fact] public async Task PipelineLimitsRecordsAndFiltersOnlyAssignedPositions(){
        await using var host=new Host();using var client=await Login(host,"Recruiter");
        using(var scope=host.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
            var p=new Position{Reference="PAGE-A",Title="Assigned First Role",AssignedRecruiter="HttpUser"};
            var second=new Position{Reference="PAGE-B",Title="Assigned Second Role",AssignedRecruiter="HttpUser"};
            db.Positions.AddRange(p,second,new Position{Reference="PRIVATE",Title="Unassigned Secret Role",AssignedRecruiter="someone"});
            for(int i=0;i<13;i++){var c=new Candidate{Name="Paging Candidate "+i};db.Candidates.Add(c);db.Assessments.Add(new Assessment{State="Saved",CandidateId=c.Id,CandidateName=c.Name,PositionId=i==12?second.Id:p.Id});}
            await db.SaveChangesAsync();
        }
        var all=await client.GetStringAsync("/pipeline");
        Assert.Contains("aria-label=\"Position\"",all);Assert.Contains("Assigned Second Role",all);Assert.DoesNotContain("Unassigned Secret Role",all);
        Assert.Equal(10,Regex.Matches(all,"aria-label=\"Select Paging Candidate").Count);Assert.Contains("Of 13 Records",all);Assert.Contains("Page 1 Of 2",all);
        var filtered=await client.GetStringAsync("/pipeline?ref=PAGE-B");Assert.Single(Regex.Matches(filtered,"aria-label=\"Select Paging Candidate"));Assert.Contains("Paging Candidate 12",filtered);Assert.Contains("Of 1 Records",filtered);
    }
    [Fact]public async Task EmployerPagesDocumentsAndReportsRemainWithinTheirOwnedWorkspace(){
        await using var host=new Host();using var client=await Login(host,"Employer");Guid ownDoc,privateDoc,ownApplication,privateApplication;
        using(var scope=host.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var owner=(await db.Users.SingleAsync(u=>u.UserName=="HttpUser")).Id;var other=new AtsUser{UserName="OtherEmployer",Email="other-owner@example.test",EmailConfirmed=true};db.Users.Add(other);db.UserRoles.Add(new IdentityUserRole<string>{UserId=other.Id,RoleId=(await db.Roles.SingleAsync(r=>r.Name=="Employer")).Id});
            var w=new EmployerWorkspace{OwnerId=owner,CompanyName="Owned Employer Company"};var otherW=new EmployerWorkspace{OwnerId=other.Id,CompanyName="Other Employer Company"};db.EmployerWorkspaces.AddRange(w,otherW);
            var ownClient=new Client{WorkspaceId=w.Id,Name="Owned Employer Client",NormalizedName="OWNED"};var otherClient=new Client{WorkspaceId=otherW.Id,Name="Hidden Other Client",NormalizedName="OTHER"};db.Clients.AddRange(ownClient,otherClient);
            var doc=new StoredDocument{FileName="owned.txt",Content="Owned JD"u8.ToArray(),ContentType="text/plain"};var secret=new StoredDocument{FileName="private.txt",Content="Private JD"u8.ToArray(),ContentType="text/plain"};db.Documents.AddRange(doc,secret);ownDoc=doc.Id;privateDoc=secret.Id;
            var p=new Position{WorkspaceId=w.Id,ClientId=ownClient.Id,Client=ownClient.Name,Title="Owned Employer Position",Reference="OWN",AssignedRecruiter="team-member",JdDocumentId=doc.Id};var otherP=new Position{WorkspaceId=otherW.Id,ClientId=otherClient.Id,Client=otherClient.Name,Title="Hidden Other Position",Reference="OTHER",AssignedRecruiter="HttpUser",JdDocumentId=secret.Id};var legacy=new Position{Title="Hidden Legacy Position",Reference="LEGACY",AssignedRecruiter="HttpUser"};db.Positions.AddRange(p,otherP,legacy);
            var c=new Candidate{Name="Owned Employer Candidate"};var otherC=new Candidate{Name="Hidden Other Candidate"};db.Candidates.AddRange(c,otherC);var a=new Assessment{State="Saved",PositionId=p.Id,CandidateId=c.Id,CandidateName=c.Name,SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"","","",ownClient.Name,p.Title,p.Reference))};var secretA=new Assessment{State="Saved",PositionId=otherP.Id,CandidateId=otherC.Id,CandidateName=otherC.Name};db.Assessments.AddRange(a,secretA);ownApplication=a.Id;privateApplication=secretA.Id;await db.SaveChangesAsync();
        }
        foreach(var route in new[]{"/employer","/clients","/positions","/pipeline","/assessment","/candidates","/search?q=Employer"}){var response=await client.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("Hidden Other",html);Assert.DoesNotContain("Hidden Legacy",html);Assert.DoesNotContain("The page could not load",html);}
        Assert.Contains("Owned Employer Position",await client.GetStringAsync("/positions"));Assert.Contains("+ Create Client",WebUtility.HtmlDecode(await client.GetStringAsync("/clients")));Assert.Contains("Owned Employer Candidate",await client.GetStringAsync("/pipeline"));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/files/"+ownDoc)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/files/"+privateDoc)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/reports?ids="+ownApplication)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/reports?ids="+privateApplication)).StatusCode);
        var settings=await client.GetStringAsync("/settings");Assert.Contains("My Profile",settings);Assert.Contains("Settings sections",settings);Assert.DoesNotContain("Add a Sub-Admin",settings);Assert.DoesNotContain("Assessment Decision Rules</b>",settings);
        var templates=await client.GetStringAsync("/settings/email-templates");Assert.Contains("Email Templates Access Required",templates);Assert.DoesNotContain("+ Create Template",templates);
    }
    [Fact] public async Task AuthenticatedWorkspacesAndHistoricalReportsRenderStoredEvidence()
    {
        await using var host=new Host();using var client=await Login(host);Guid applicationId,candidateId;using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var p=new Position{Reference="001",Title="Test role",Client="Test client",Criteria=[new(){Name="Experience",Weight=100}]};var c=new Candidate{LegacyId="CAND-001",Name="HTTP Candidate",Email="secret-contact@example.test",Phone="123SECRET",Summary="Profile summary"};db.Positions.Add(p);db.Candidates.Add(c);var a=new Assessment{PositionId=p.Id,CandidateId=c.Id,State="Saved",CandidateName=c.Name,Stage="Shortlisted",Decision="Shortlist",SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"Historical summary","","",p.Client,p.Title,p.Reference))};db.Assessments.Add(a);var v=DatabaseInitializer.VersionOf(a);v.Decision="Hold";v.MatchPercent=55;db.AssessmentVersions.Add(v);await db.SaveChangesAsync();applicationId=a.Id;candidateId=c.Id;}
        foreach(var route in new[]{"/","/clients","/positions?ref=001","/assessment","/candidates","/talent","/pipeline?active=true&queue=Shortlisted","/settings","/import",$"/candidates?id={candidateId}",$"/assessment/{applicationId}"}){var response=await client.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var rendered=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("The page could not load",rendered);Assert.DoesNotContain("Application.Decision",rendered);Assert.DoesNotContain("a.Decision",rendered);Assert.DoesNotContain("disabled=\"Busy\"",rendered);Assert.DoesNotContain("role=\"alert\">Error",rendered);}
        var report=await client.GetStringAsync($"/reports?ids={applicationId}&version=1");Assert.Contains("55%",report);Assert.Contains("Recommendation: Hold",report);Assert.Contains("no criterion results were stored",report);Assert.DoesNotContain("secret-contact",report);Assert.DoesNotContain("123SECRET",report);
        using var checkScope=host.Services.CreateScope();var check=checkScope.ServiceProvider.GetRequiredService<AtsDbContext>();Assert.Equal("Shortlist",(await check.Assessments.SingleAsync()).Decision);Assert.Contains(await check.Audit.ToListAsync(),x=>x.Action=="Report generated");
    }
    sealed class AdminState(string id):AuthenticationStateProvider {public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,"HttpUser"),new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Role,"Admin")],"Test"))));}

    [Fact] public async Task RecruiterPagesHideCreationAndPaidPoolProtectsProfilesAndFiles(){
        await using var host=new Host();using var client=await Login(host,"Recruiter");
        Guid candidateId,docId,applicationId;
        using(var scope=host.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
            var doc=new StoredDocument{FileName="pool.txt",Content="Paid pool CV"u8.ToArray()};db.Documents.Add(doc);
            var candidate=new Candidate{Name="Pool Only Candidate",DocumentId=doc.Id,Notes="Private Admin Note"};db.Candidates.Add(candidate);
            var position=new Position{Reference="Other",Title="Confidential Other Role",AssignedRecruiter="someone"};
            db.Positions.Add(position);var a=new Assessment{PositionId=position.Id,CandidateId=candidate.Id,State="Saved",CandidateName=candidate.Name,OwnerId=(await db.Users.SingleAsync(x=>x.UserName=="HttpUser")).Id};
            db.Assessments.Add(a);await db.SaveChangesAsync();candidateId=candidate.Id;docId=doc.Id;applicationId=a.Id;
        }
        Assert.DoesNotContain("+ Create Position",await client.GetStringAsync("/positions"));
        Assert.DoesNotContain("+ Create Client",await client.GetStringAsync("/clients"));
        var locked=await client.GetStringAsync("/talent");Assert.Contains("Workspace Talent Pool Access Is Disabled",locked);Assert.DoesNotContain("Pool Only Candidate",locked);
        Assert.DoesNotContain("Pool Only Candidate",await client.GetStringAsync("/candidates"));
        Assert.DoesNotContain("Pool Only Candidate",await client.GetStringAsync("/search?q=Pool"));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/files/{docId}")).StatusCode);
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.Users.SingleAsync(x=>x.UserName=="HttpUser")).TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(1);await db.SaveChangesAsync();}
        var paid=await client.GetStringAsync($"/talent?id={candidateId}");Assert.Contains("Pool Only Candidate",paid);Assert.DoesNotContain("Private Admin Note",paid);Assert.DoesNotContain("Confidential Other Role",paid);
        Assert.DoesNotContain("The page could not load",paid);Assert.DoesNotContain("Edit profile",paid);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/files/{docId}")).StatusCode);
        var privateAssessment=await client.GetStringAsync($"/assessment/{applicationId}");Assert.Contains("Assessment not found",privateAssessment);Assert.DoesNotContain("Confidential Other Role",privateAssessment);
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.Users.SingleAsync(x=>x.UserName=="HttpUser")).TalentPoolPaidUntilUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/files/{docId}")).StatusCode);
        Assert.Contains("Workspace Talent Pool Access Is Disabled",await client.GetStringAsync("/talent"));
    }


    [Fact] public async Task HiringReviewLinkRequiresItsOwnCookieAndFeedbackRequiresAntiforgery(){
        await using var host=new Host();using var recruiter=await Login(host);
        Guid appId,contactId;string adminId;
        using(var scope=host.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();adminId=(await db.Users.SingleAsync(u=>u.UserName=="HttpUser")).Id;
            var client=new Client{Name="Review Client",NormalizedName="REVIEW CLIENT"};db.Clients.Add(client);
            var contact=new ClientContact{ClientId=client.Id,Name="Hiring Contact",Email="manager@example.test"};db.ClientContacts.Add(contact);contactId=contact.Id;
            var position=new Position{Reference="Review",Title="Review Role",Client=client.Name,ClientId=client.Id};db.Positions.Add(position);
            var candidate=new Candidate{Name="Review Candidate",Email="PRIVATE-CANDIDATE@example.test",Phone="+923001112222"};db.Candidates.Add(candidate);
            var a=new Assessment{State="Saved",PositionId=position.Id,CandidateId=candidate.Id,Stage="Shortlisted",SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(candidate,null,[],"PRIVATE-CANDIDATE@example.test +923001112222","","",client.Name,position.Title,position.Reference))};db.Assessments.Add(a);await db.SaveChangesAsync();appId=a.Id;
        }
        using var serviceScope=host.Services.CreateScope();
        var service=new HiringReviewService(host.Services.GetRequiredService<IDbContextFactory<AtsDbContext>>(),new Access(new AdminState(adminId)),host.Services.GetRequiredService<IDataProtectionProvider>(),host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),serviceScope.ServiceProvider.GetRequiredService<IAccountEmail>(),host.Services.GetRequiredService<RecruitmentUpdates>());
        var share=await service.Forward(contactId,[appId],"",false);
        using var guest=host.CreateClient(new(){AllowAutoRedirect=false});Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync("/review/data")).StatusCode);
        var access=await guest.GetAsync(new Uri(share.Link).PathAndQuery);Assert.Equal(HttpStatusCode.Redirect,access.StatusCode);Assert.Equal("/review",access.Headers.Location!.ToString());
        Assert.Contains(access.Headers.GetValues("Set-Cookie"),value=>value.Contains("httponly",StringComparison.OrdinalIgnoreCase));
        var html=await guest.GetStringAsync("/review");Assert.Contains("Select Position",html);Assert.Contains("Interview Preferences",html);Assert.Contains("<th>AI Decision</th>",html);Assert.DoesNotContain("<th>Client Status</th>",html);Assert.DoesNotContain("<th>Interview Status</th>",html);
        var comparison=await guest.GetStringAsync("/review/comparison?position="+JsonDocument.Parse(await guest.GetStringAsync("/review/data")).RootElement.GetProperty("rows")[0].GetProperty("positionId").GetString());Assert.Contains("Candidate Summary",comparison);Assert.Contains("Must-Have Fit",comparison);Assert.Contains("comparison-report",comparison);
        var payload=await guest.GetStringAsync("/review/data");Assert.Contains("Review Candidate",payload);Assert.DoesNotContain("PRIVATE-CANDIDATE",payload);Assert.DoesNotContain("923001112222",payload);
        using var data=JsonDocument.Parse(payload);var row=data.RootElement.GetProperty("rows")[0];var id=row.GetProperty("id").GetGuid();
        var report=await guest.GetAsync("/review/report?submission="+id);var reportHtml=await report.Content.ReadAsStringAsync();Assert.DoesNotContain("PRIVATE-CANDIDATE",reportHtml);Assert.DoesNotContain("923001112222",reportHtml);Assert.Equal("no-store",report.Headers.CacheControl!.ToString());
        var feedback=new HiringFeedback(id,row.GetProperty("revision").GetGuid(),row.GetProperty("applicationRevision").GetGuid(),"Shortlist","Proceed",null,"","",30,"");
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/review/feedback",feedback)).StatusCode);
        guest.DefaultRequestHeaders.Add("RequestVerificationToken",Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/review/feedback",feedback)).StatusCode);
        Assert.Contains("In Review",await recruiter.GetStringAsync("/pipeline"));
        Assert.Equal(HttpStatusCode.Conflict,(await guest.PostAsJsonAsync("/review/feedback",feedback)).StatusCode);
        var interviews=new InterviewService(host.Services.GetRequiredService<IDbContextFactory<AtsDbContext>>(),new Access(new AdminState(adminId)),host.Services.GetRequiredService<IDataProtectionProvider>(),host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),serviceScope.ServiceProvider.GetRequiredService<IAccountEmail>(),host.Services.GetRequiredService<RecruitmentUpdates>());
        Guid appRevision;using(var scope=host.Services.CreateScope())appRevision=(await scope.ServiceProvider.GetRequiredService<AtsDbContext>().Assessments.SingleAsync(a=>a.Id==appId)).Revision;
        var invitation=await interviews.Invite(new(appId,appRevision,null,null,"candidate@example.test","",DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm"),"UTC",30,"Online","Manual Link","https://meet.example.test/room","","Interview Invitation"));
        using var candidateClient=host.CreateClient(new(){AllowAutoRedirect=false});Assert.Equal(HttpStatusCode.Unauthorized,(await candidateClient.GetAsync("/interview/data")).StatusCode);
        var invitationAccess=await candidateClient.GetAsync(new Uri(invitation.Link).PathAndQuery);Assert.Equal(HttpStatusCode.Redirect,invitationAccess.StatusCode);Assert.Contains(invitationAccess.Headers.GetValues("Set-Cookie"),c=>c.Contains("httponly",StringComparison.OrdinalIgnoreCase));
        var candidatePage=await candidateClient.GetStringAsync("/interview");var candidateData=await candidateClient.GetStringAsync("/interview/data");Assert.DoesNotContain("candidate@example.test",candidateData);using var candidateJson=JsonDocument.Parse(candidateData);var interview=candidateJson.RootElement.GetProperty("interview");var response=new InterviewResponse(interview.GetProperty("id").GetGuid(),interview.GetProperty("revision").GetGuid(),"Accepted","",null,"UTC");
        Assert.Equal(HttpStatusCode.BadRequest,(await candidateClient.PostAsJsonAsync("/interview/respond",response)).StatusCode);
        candidateClient.DefaultRequestHeaders.Add("RequestVerificationToken",Regex.Match(candidatePage,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.Equal(HttpStatusCode.Unauthorized,(await candidateClient.PostAsJsonAsync("/interview/respond",response with{InterviewId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await candidateClient.PostAsJsonAsync("/interview/respond",response)).StatusCode);Assert.Contains("Confirmed",await guest.GetStringAsync("/review/data"));
        await service.Revoke(id);Assert.Equal(HttpStatusCode.Unauthorized,(await candidateClient.GetAsync("/interview/data")).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync("/review/report?submission="+id)).StatusCode);
    }

    [Fact] public async Task AdministratorCanActivateImportedAccountSetPasswordAndChangeGrants()
    {
        await using var host=new Host();using var client=await Login(host);string adminId,importedId;using(var scope=host.Services.CreateScope()){var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();adminId=(await manager.FindByNameAsync("HttpUser"))!.Id;var imported=new AtsUser{UserName="Imported",Email="imported@example.test",Status="Pending"};Assert.True((await manager.CreateAsync(imported)).Succeeded);Assert.True((await manager.AddToRoleAsync(imported,"Recruiter")).Succeeded);importedId=imported.Id;}
        var service=new AdminService(new Access(new AdminState(adminId)),host.Services.GetRequiredService<IServiceScopeFactory>(),host.Services.GetRequiredService<IDbContextFactory<AtsDbContext>>());var initial=(await service.Users()).Single(x=>x.Id==importedId);Assert.False(initial.HasPassword);await service.ResetPassword(importedId,"Imported-password!123");await service.UpdateUser(initial with{Status="Active",CanExportReports=false,FullName="Imported Recruiter"});var actual=(await service.Users()).Single(x=>x.Id==importedId);Assert.True(actual.HasPassword);Assert.Equal("Active",actual.Status);Assert.False(actual.CanExportReports);
        var admin=(await service.Users()).Single(x=>x.Id==adminId);await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UpdateUser(admin with{Status="Inactive"}));using var check=host.Services.CreateScope();var users=check.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(importedId))!,"Imported-password!123"));
    }
    [Fact] public async Task ReportGrantAndInactiveStatusProtectFileAndReportEndpointsImmediately()
    {
        await using var host=new Host();using var client=await Login(host,"Recruiter");Guid docId;using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var u=await db.Users.SingleAsync(x=>x.UserName=="HttpUser");u.CanExportReports=false;var doc=new StoredDocument{FileName="test.txt",Content="test"u8.ToArray()};db.Documents.Add(doc);await db.SaveChangesAsync();docId=doc.Id;}
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/reports?ids={Guid.NewGuid()}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/files/{docId}")).StatusCode);
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();db.Positions.Add(new(){Reference="Assigned",Title="Assigned Role",AssignedRecruiter="HttpUser",JdDocumentId=docId});await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/files/{docId}")).StatusCode);
        using(var scope=host.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();(await db.Users.SingleAsync(x=>x.UserName=="HttpUser")).Status="Inactive";await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/files/{docId}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/reports?ids={Guid.NewGuid()}")).StatusCode);
    }
}
