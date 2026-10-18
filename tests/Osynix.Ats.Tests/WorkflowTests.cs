using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;
namespace Osynix.Ats.Tests;
public class WorkflowTests
{
    sealed class State(string id="admin",string role="Admin"):AuthenticationStateProvider {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,id),new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Role,role)],"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Fixture(bool separateConnections=false):IAsyncDisposable {
        public SqliteConnection Connection=new(separateConnections?$"Data Source=assessment-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared":"Data Source=:memory:");public Factory Factory=null!;
        public async Task Init(){await Connection.OpenAsync();var options=new DbContextOptionsBuilder<AtsDbContext>();if(separateConnections)options.UseSqlite(Connection.ConnectionString);else options.UseSqlite(Connection);Factory=new(options.Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);}
        public ValueTask DisposeAsync()=>Connection.DisposeAsync();
    }
    [Fact] public async Task PositionNotesRemainScopedAfterOneOfTheCandidatesPositionsIsReassigned(){
        await using var f=new Fixture();await f.Init();var candidate=new Candidate{Name="Shared Candidate"};var first=new Position{Reference="N01",Title="First Role",AssignedRecruiter="recruiter"};var second=new Position{Reference="N02",Title="Second Role",AssignedRecruiter="recruiter"};var firstApplication=new Assessment{CandidateId=candidate.Id,PositionId=first.Id,State="Saved"};var secondApplication=new Assessment{CandidateId=candidate.Id,PositionId=second.Id,State="Saved"};
        await using(var db=f.Factory.CreateDbContext()){db.Users.Add(new(){Id="recruiter",UserName="recruiter",AccountType=AccountTypes.Legacy});db.Roles.Add(new IdentityRole("Recruiter"){Id="note-role"});db.UserRoles.Add(new(){UserId="recruiter",RoleId="note-role"});db.AddRange(candidate,first,second,firstApplication,secondApplication);await db.SaveChangesAsync();}
        var workspace=new WorkspaceService(f.Factory,new Access(new State("recruiter","Recruiter"),f.Factory));await workspace.AddNote(candidate.Id,firstApplication.Id,"First position only");var original=Assert.Single(await workspace.Notes(candidate.Id));await using(var db=f.Factory.CreateDbContext()){(await db.Positions.SingleAsync(p=>p.Id==first.Id)).AssignedRecruiter="someone-else";await db.SaveChangesAsync();}
        Assert.Empty(await workspace.Notes(candidate.Id));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>workspace.EditNote(original.Id,"Attempted edit after reassignment"));await workspace.AddNote(candidate.Id,secondApplication.Id,"Second position only");Assert.Equal(secondApplication.Id,Assert.Single(await workspace.Notes(candidate.Id)).AssessmentId);await using var verify=f.Factory.CreateDbContext();Assert.Equal("First position only",(await verify.CandidateNotes.SingleAsync(n=>n.Id==original.Id)).Text);
    }
    [Fact] public async Task ImportRetainsDistinctPeopleEvidenceDatesNullMetricsAndUtcAudit()
    {
        await using var f=new Fixture();await f.Init();using var wb=new XLWorkbook();
        static void Headers(IXLWorksheet s,params string[] names){for(int i=0;i<names.Length;i++)s.Cell(1,i+1).Value=names[i];}
        var p=wb.AddWorksheet("Positions Master");Headers(p,"Ref #","Position","Client","Open Date","Priority","Hiring Target");p.Cell(2,1).Value="001";p.Cell(2,2).Value="Role";p.Cell(2,3).Value="Client";p.Cell(2,4).Value=new DateTime(2026,1,2);p.Cell(2,5).Value="High";p.Cell(2,6).Value=2;
        var c=wb.AddWorksheet("JD Criteria");Headers(c,"Ref #","Criterion","Weight %");c.Cell(2,1).Value="001";c.Cell(2,2).Value="Criterion";c.Cell(2,3).Value=100;
        var t=wb.AddWorksheet("Talent Pool Master");Headers(t,"Candidate ID","Candidate Name","Employment / Career History","Career Profile Summary");
        t.Cell(2,1).Value="CAND-001";t.Cell(2,2).Value="Same Name";t.Cell(2,3).Value="""[{"company_name":"Company","designation":"Manager","start_date":"2020","end_date":"Present"}]""";t.Cell(2,4).Value="Career summary";t.Cell(3,1).Value="CAND-002";t.Cell(3,2).Value="Same Name";
        var a=wb.AddWorksheet("Candidates");Headers(a,"Candidate ID","Candidate Name","Ref #","Date Screened","ATS Match %","Criterion Results JSON","Recruitment Stage");
        a.Cell(2,1).Value="CAND-001";a.Cell(2,2).Value="Same Name";a.Cell(2,3).Value="001";a.Cell(2,4).Value=new DateTime(2026,2,3,10,30,0);a.Cell(2,5).Value="60%";a.Cell(2,6).Value="""[{"criterion":"Criterion","weight":100,"knockout":"Yes","score10":6,"evidenceConfidence10":8,"evidenceStatus":"Pass","evidenceFound":"Source evidence","assessmentRationale":"Source rationale"}]""";a.Cell(2,7).Value="Consider";
        a.Cell(3,1).Value="CAND-002";a.Cell(3,2).Value="Same Name";a.Cell(3,3).Value="001";
        var log=wb.AddWorksheet("Activity Log");Headers(log,"Timestamp","Action","Entity ID / Ref #");log.Cell(2,1).Value=new DateTime(2026,2,3,10,30,0);log.Cell(2,2).Value="PROFILE_UPDATED";log.Cell(2,3).Value="CAND-001";
        wb.AddWorksheet("Lists & Settings");using var stream=new MemoryStream();wb.SaveAs(stream);await new WorkbookImport(f.Factory,new Access(new State())).Commit(stream.ToArray());
        await using var db=f.Factory.CreateDbContext();var apps=await db.Assessments.Include(x=>x.Candidate).ToListAsync();Assert.Equal(2,await db.Candidates.CountAsync());Assert.Equal(2,apps.Count);
        var first=apps.Single(x=>x.Candidate!.LegacyId=="CAND-001");Assert.Equal("Consider",first.Stage);Assert.Equal(new DateTime(2026,2,3,10,30,0),first.DateScreened);Assert.Equal(new DateTime(2026,2,3,5,30,0),first.CreatedUtc);
        var snap=JsonSerializer.Deserialize<AssessmentSnapshot>(first.SnapshotJson)!;Assert.Equal(6,snap.Criteria.Single().Score);Assert.Equal(8,snap.Criteria.Single().Confidence);Assert.Equal("Source evidence",snap.Criteria.Single().Found);
        var second=apps.Single(x=>x.Candidate!.LegacyId=="CAND-002");Assert.Null(second.MatchPercent);Assert.Empty(JsonSerializer.Deserialize<AssessmentSnapshot>(second.SnapshotJson)!.Criteria);Assert.Single(await db.EmploymentHistory.ToListAsync());Assert.Equal(new DateTime(2026,2,3,5,30,0),(await db.Audit.SingleAsync(x=>x.Action=="PROFILE_UPDATED")).CreatedUtc);
        Assert.Equal(2,await db.AssessmentVersions.CountAsync());
    }
    sealed class Provider:HttpMessageHandler {
        public int Calls;public string Request="";public bool FailFirst;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;Request=await request.Content!.ReadAsStringAsync(ct);if(FailFirst&&Calls==1)return new(HttpStatusCode.ServiceUnavailable);var output=new{candidate_name="Person",email_id="person@example.test",mobile_phone_number="",current_designation="Stale inferred role",current_company="Wrong company",location="Karachi",total_experience_years=6,relevant_industry_experience_years=6,talent_profile=new{primary_function="Operations",secondary_function_specialization="",industries_domains="Manufacturing",core_skills="Planning",seniority_level="Manager",systems_tools="",geographic_market_exposure="",professional_summary="Permanent facts",career_profile_summary="Career facts",employment_career_history=new[]{new{company_name="Actual Company",designation="Actual Manager",start_date="2020",end_date="Present",location="Karachi"}},education=Array.Empty<object>()},source_evidence=new[]{new{fact="Worked as manager",source="Employment history"}},criterion_results=new[]{new{criterion="Experience",score_10=8,evidence_confidence_10=10,evidence_found="Employment record",assessment_rationale="Direct evidence"}}};return new(HttpStatusCode.OK){Content=JsonContent.Create(new{status="completed",output=new[]{new{content=new[]{new{type="output_text",text=JsonSerializer.Serialize(output)}}}}})};}
    }
    sealed class ProfileProvider:HttpMessageHandler {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;var output=new{current_designation="Senior Recruiter",current_company="Osynix Group",total_experience_years=7,talent_profile=new{primary_function="Talent Acquisition",secondary_function_specialization="Technology Hiring",industries_domains="Software",core_skills="Sourcing, Interviewing",seniority_level="Senior",systems_tools="ATS",geographic_market_exposure="Lahore",career_profile_summary="Seven years of recruitment experience.",professional_summary="Senior recruiter specializing in technology roles."},employment_career_history=new[]{new{company_name="Osynix Group",designation="Senior Recruiter",start_date="2021",end_date="Present",location="Lahore"}},education=new[]{new{qualification="MBA",institution="City University",start_date="2018",end_date="2020",location="Lahore"}},source_evidence=new[]{new{fact="Senior recruiter since 2021",source="Employment History"}}};return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{status="completed",output=new[]{new{content=new[]{new{type="output_text",text=JsonSerializer.Serialize(output)}}}}})});}
    }
    sealed class AnonymousState:AuthenticationStateProvider {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CareersApplicationAutomaticallyAssessesAndFillsProfileOrRetainsApplicationOnProviderFailure(bool failProvider){
        await using var f=new Fixture(separateConnections:true);await f.Init();
        var workspace=new EmployerWorkspace{OwnerId="owner",Kind=AccountTypes.Freelance,CompanyName="Test Workspace",CareersPublished=true};
        var position=new Position{WorkspaceId=workspace.Id,Reference="AUTO",Title="Operations Manager",Client="Test Workspace",Status="Active",CareersPublished=true,CriteriaLocked=true,PublicJobDescription="Operations role",Criteria=[new(){Name="Experience",Weight=100}]};
        await using(var db=f.Factory.CreateDbContext()){
            db.Users.Add(new AtsUser{Id="owner",UserName="owner@example.test",AccountType=AccountTypes.Freelance,EmailConfirmed=true});
            db.EmployerWorkspaces.Add(workspace);db.Positions.Add(position);db.DecisionRules.Add(new(){MustHaveFit="Pass",MinimumMatch=0,Decision="Shortlist"});await db.SaveChangesAsync();
        }
        var provider=new Provider{FailFirst=failProvider};
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OpenAI:ApiKey","test-key"},{"OpenAI:Model","test-model"}}).Build();
        var anonymous=new AnonymousState();var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IDbContextFactory<AtsDbContext>>(f.Factory);
        services.AddScoped(_=>new AiService(new HttpClient(provider),config,f.Factory,new Access(anonymous,f.Factory),new DatabaseDocumentStore(f.Factory)));
        using var root=services.BuildServiceProvider();var updates=new RecruitmentUpdates();
        using var worker=new PublicApplicationAssessmentQueue(root.GetRequiredService<IServiceScopeFactory>(),f.Factory,updates,NullLogger<PublicApplicationAssessmentQueue>.Instance);
        var intake=new EmployerService(anonymous,new Access(anonymous,f.Factory),f.Factory,updates,automaticAssessments:worker);
        var submitted=await intake.ApplyPublic(workspace.Id,position.Id,"Form Name","form@example.test","+92 300 1234567","Lahore","candidate.txt","Synthetic professional CV"u8.ToArray());
        Assert.True(submitted.ProfilePending);
        // Start after submission to also exercise durable recovery and duplicate queue entries.
        await worker.StartAsync(CancellationToken.None);
        var deadline=DateTime.UtcNow.AddSeconds(15);string status="Pending";
        while(status=="Pending"&&DateTime.UtcNow<deadline){await Task.Delay(50);await using var db=f.Factory.CreateDbContext();status=(await db.Assessments.AsNoTracking().SingleAsync()).AutoAssessmentStatus;}
        await worker.StopAsync(CancellationToken.None);
        await using var check=f.Factory.CreateDbContext();var application=await check.Assessments.SingleAsync();var candidate=await check.Candidates.Include(c=>c.EmploymentHistory).SingleAsync();
        Assert.True(provider.Calls==1,application.AutoAssessmentError);Assert.Equal(submitted.Id,application.Id);Assert.Equal("Saved",application.State);Assert.Equal("Applied",application.Stage);
        Assert.Equal("Form Name",candidate.Name);Assert.Equal("form@example.test",candidate.Email);Assert.Equal("+92 300 1234567",candidate.Phone);Assert.Equal("Lahore",candidate.City);
        Assert.Single(await check.Documents.ToListAsync());Assert.Equal(application.DocumentId,candidate.DocumentId);
        if(failProvider){
            Assert.Equal("Failed",application.AutoAssessmentStatus);Assert.Null(application.MatchPercent);Assert.Equal("Not Assessed",application.Decision);Assert.Contains("503",application.AutoAssessmentError);Assert.Equal("Failed",candidate.CvProfileStatus);Assert.Empty(await check.AssessmentVersions.ToListAsync());
        }else{
            Assert.Equal("Ready",application.AutoAssessmentStatus);Assert.Equal(80,application.MatchPercent);Assert.NotEqual("Not Assessed",application.Decision);Assert.Equal("test-model",application.Model);Assert.NotNull(application.LastAssessedDate);
            Assert.Equal("Actual Manager",candidate.CurrentRole);Assert.Equal("Actual Company",candidate.CurrentCompany);Assert.Equal("Permanent facts",candidate.Summary);Assert.Equal("Ready",candidate.CvProfileStatus);Assert.Single(candidate.EmploymentHistory);Assert.Single(await check.AssessmentVersions.ToListAsync());
            var snapshot=JsonSerializer.Deserialize<AssessmentSnapshot>(application.SnapshotJson)!;Assert.Equal("form@example.test",snapshot.Profile.Email);Assert.Equal("Lahore",snapshot.Profile.City);Assert.Single(snapshot.Criteria);
        }
    }
    [Fact] public async Task PublicCvProfileIsExtractedBeforeAssessmentAndKeepsApplicationIdentityAuthoritative(){
        await using var f=new Fixture();await f.Init();var person=new Candidate{Name="Applicant Supplied Name",Email="applicant@example.test",Phone="+92 300 1234567",City="Karachi",Location="Karachi",CvProfileStatus="Pending"};await using(var db=f.Factory.CreateDbContext()){db.Candidates.Add(person);await db.SaveChangesAsync();}
        var provider=new ProfileProvider();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OpenAI:ApiKey","test-key"},{"OpenAI:Model","test-model"}}).Build();var ai=new AiService(new HttpClient(provider),config,f.Factory,new Access(new State()),new DatabaseDocumentStore(f.Factory));
        await ai.ExtractPublicCvProfile(person.Id,"applicant.txt","Synthetic professional CV"u8.ToArray());
        await using var check=f.Factory.CreateDbContext();var saved=await check.Candidates.Include(x=>x.EmploymentHistory).Include(x=>x.EducationHistory).SingleAsync();Assert.Equal(1,provider.Calls);Assert.Equal("Applicant Supplied Name",saved.Name);Assert.Equal("applicant@example.test",saved.Email);Assert.Equal("+92 300 1234567",saved.Phone);Assert.Equal("Karachi",saved.City);Assert.Equal("Senior Recruiter",saved.CurrentRole);Assert.Equal("Osynix Group",saved.CurrentCompany);Assert.Equal("Senior recruiter specializing in technology roles.",saved.Summary);Assert.Equal("Ready",saved.CvProfileStatus);Assert.Equal("Lahore",saved.EmploymentHistory.Single().Location);Assert.Equal("MBA",saved.EducationHistory.Single().Qualification);Assert.Contains("source",saved.IntelligenceJson);
    }
    [Fact] public async Task CvProfileWorkerExtractsQueuedProfilesInBackground(){
        await using var f=new Fixture();await f.Init();var document=new StoredDocument{FileName="applicant.txt",Content="Synthetic professional CV"u8.ToArray(),ContentType="text/plain"};var person=new Candidate{Name="Applicant Name",Email="applicant@example.test",Phone="+92 300 1234567",City="Karachi",Location="Karachi",DocumentId=document.Id,CvProfileStatus="Pending"};await using(var db=f.Factory.CreateDbContext()){db.Documents.Add(document);db.Candidates.Add(person);await db.SaveChangesAsync();}
        var api=new ProfileProvider();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OpenAI:ApiKey","test-key"},{"OpenAI:Model","test-model"}}).Build();var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IDbContextFactory<AtsDbContext>>(f.Factory);services.AddScoped<IDocumentStore>(_=>new DatabaseDocumentStore(f.Factory));services.AddScoped(_=>new AiService(new HttpClient(api),config,f.Factory,new Access(new State()),new DatabaseDocumentStore(f.Factory)));using var root=services.BuildServiceProvider();var updates=new RecruitmentUpdates();using var worker=new CvProfileExtractionQueue(root.GetRequiredService<IServiceScopeFactory>(),f.Factory,updates,NullLogger<CvProfileExtractionQueue>.Instance);await worker.StartAsync(CancellationToken.None);
        var deadline=DateTime.UtcNow.AddSeconds(10);Candidate? extracted=null;while(DateTime.UtcNow<deadline){await using var db=f.Factory.CreateDbContext();extracted=await db.Candidates.AsNoTracking().SingleAsync(c=>c.Id==person.Id);if(extracted.CvProfileStatus!="Pending")break;await Task.Delay(50);}await worker.StopAsync(CancellationToken.None);
        Assert.Equal(1,api.Calls);Assert.Equal("Ready",extracted!.CvProfileStatus);Assert.Equal("Senior Recruiter",extracted.CurrentRole);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoredReassessmentMakesOneProviderCallAndSavePreservesMasterFacts(bool paidRecruiter)
    {
        await using var f=new Fixture();await f.Init();var position=new Position{Reference="001",Client="Client",Title="Role",AssignedRecruiter=paidRecruiter?"paid":"",CriteriaLocked=true,Criteria=[new(){Name="Experience",Weight=100,Knockout=true}]};var person=new Candidate{Name="Person",Email="person@example.test",LegacyId="CAND-001",Summary="Original master summary",IntelligenceJson="[{\"fact\":\"Manager since 2020\",\"source\":\"CV\"}]"};
        await using(var db=f.Factory.CreateDbContext()){if(paidRecruiter){db.Users.Add(new(){Id="paid",UserName="paid",TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(1)});db.Roles.Add(new IdentityRole("Recruiter"){Id="paid-role"});db.UserRoles.Add(new(){UserId="paid",RoleId="paid-role"});}db.Positions.Add(position);db.Candidates.Add(person);db.DecisionRules.Add(new(){MustHaveFit="Pass",MinimumMatch=60,Decision="Shortlist"});await db.SaveChangesAsync();}
        var provider=new Provider();var access=paidRecruiter?new Access(new State("paid","Recruiter"),f.Factory):new Access(new State());var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OpenAI:ApiKey","test-key"},{"OpenAI:Model","test-model"}}).Build();
        var ai=new AiService(new HttpClient(provider),config,f.Factory,access,new DatabaseDocumentStore(f.Factory));var draft=await ai.AssessStored(person.Id,position.Id);Assert.Equal(1,provider.Calls);Assert.Contains("STORED VACANCY-INDEPENDENT",provider.Request);Assert.DoesNotContain("input_file",provider.Request);
        await using(var db=f.Factory.CreateDbContext()){var a=await db.Assessments.SingleAsync();var snap=JsonSerializer.Deserialize<AssessmentSnapshot>(a.SnapshotJson)!;Assert.Equal("Actual Manager",snap.Profile.CurrentRole);Assert.Equal("Actual Company",snap.Profile.CurrentCompany);Assert.Equal("2020",snap.Profile.EmploymentHistory.Single().StartDate);Assert.Equal(80,a.MatchPercent);}
        var ats=new AtsService(f.Factory,access);
        if(paidRecruiter){
            await using(var db=f.Factory.CreateDbContext()){(await db.Users.SingleAsync()).TalentPoolPaidUntilUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();}
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.SaveDraft(draft));
            await using(var db=f.Factory.CreateDbContext()){(await db.Users.SingleAsync()).TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(1);await db.SaveChangesAsync();}
        }
        await ats.SaveDraft(draft);await ats.Assessments();await ats.Versions(draft);Assert.Equal(1,provider.Calls);
        await using var check=f.Factory.CreateDbContext();Assert.Equal("Original master summary",(await check.Candidates.SingleAsync()).Summary);Assert.Empty(await check.Documents.ToListAsync());Assert.Single(await check.AssessmentVersions.ToListAsync());
    }
    [Fact] public async Task NewCandidateStructuredHistoryAndExplicitRefreshFollowCorrectIdentity()
    {
        await using var f=new Fixture();await f.Init();var p=new Position{Reference="001",Client="Client",Title="Role"};var c=new Candidate{Name="Person",Email="person@example.test",Employment="""[{"company_name":"Original","designation":"Manager","start_date":"2020","end_date":"Present"}]"""};CandidateProfile.ReadStructuredHistory(c);
        var draft=new Assessment{PositionId=p.Id,SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"","","","Client","Role","001"))};await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(p);db.Assessments.Add(draft);await db.SaveChangesAsync();}
        var ats=new AtsService(f.Factory,new Access(new State()));await ats.SaveDraft(draft.Id);Guid cid;await using(var db=f.Factory.CreateDbContext()){cid=(await db.Candidates.SingleAsync()).Id;Assert.Equal(cid,(await db.EmploymentHistory.SingleAsync()).CandidateId);}
        c.Employment="""[{"company_name":"Updated","designation":"Director","start_date":"2025","end_date":"Present"}]""";c.EmploymentHistory=[];c.Document=null;CandidateProfile.ReadStructuredHistory(c);var next=new Assessment{PositionId=p.Id,SourceCandidateId=cid,RefreshProfile=true,SnapshotJson=JsonSerializer.Serialize(new AssessmentSnapshot(c,null,[],"","","","Client","Role","001"))};
        await using(var db=f.Factory.CreateDbContext()){db.Assessments.Add(next);await db.SaveChangesAsync();}await ats.SaveDraft(next.Id);await using var check=f.Factory.CreateDbContext();Assert.Single(await check.Candidates.ToListAsync());Assert.Equal("Updated",(await check.EmploymentHistory.SingleAsync()).CompanyName);Assert.Equal("Director",(await check.Candidates.SingleAsync()).CurrentRole);
    }

    [Fact] public async Task LegacyRecruiterDefaultsAssignmentsAndPaidPoolAreEnforcedAcrossReadsAndWrites(){
        await using var f=new Fixture();await f.Init();
        var client=new Client{Name="Assigned Client",NormalizedName="ASSIGNED CLIENT"};var privateClient=new Client{Name="Other Client",NormalizedName="OTHER CLIENT"};
        var owned=new Position{Reference="001",Client=client.Name,ClientId=client.Id,Title="Assigned Role",AssignedRecruiter="recruiter",Criteria=[new(){Name="Experience",Weight=100}]};
        var other=new Position{Reference="002",Client=privateClient.Name,ClientId=privateClient.Id,Title="Other Role",CreatedBy="recruiter",AssignedRecruiter="someone"};
        var candidate=new Candidate{Name="Assigned Candidate",Notes="Admin internal note"};var pool=new Candidate{Name="Pool Candidate",Notes="Private pool note"};
        var application=new Assessment{PositionId=owned.Id,CandidateId=candidate.Id,OwnerId="recruiter",State="Saved"};
        var foreign=new Assessment{PositionId=other.Id,CandidateId=pool.Id,OwnerId="recruiter",State="Saved"};
        await using(var db=f.Factory.CreateDbContext()){
            var u=AccountRegistration.NewUser("recruiter","Recruiter","Recruiter");u.Id="recruiter";u.AccountType=AccountTypes.Legacy;u.Status="Active";u.CanViewTalentPool=true;
            db.Users.Add(u);db.Roles.Add(new IdentityRole("Recruiter"){Id="r"});db.UserRoles.Add(new(){UserId=u.Id,RoleId="r"});
            db.Clients.AddRange(client,privateClient);db.Positions.AddRange(owned,other);db.Candidates.AddRange(candidate,pool);db.Assessments.AddRange(application,foreign);await db.SaveChangesAsync();
        }
        var access=new Access(new State("recruiter","Recruiter"),f.Factory);var ats=new AtsService(f.Factory,access);var workspace=new WorkspaceService(f.Factory,access);
        Assert.False(await access.Can("Positions.Create"));Assert.False(await access.Can("Clients.Create"));Assert.False(await access.Can("Talent.View"));
        Assert.Equal(owned.Id,Assert.Single(await ats.Positions()).Id);Assert.Equal(client.Id,Assert.Single(await workspace.Clients()).Id);
        Assert.Equal(application.Id,Assert.Single(await ats.Assessments()).Id);Assert.Empty(Assert.Single(await ats.Candidates()).Notes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequirePosition(other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequireApplication(foreign.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>workspace.SaveClient(new Client{Name="Attempt"}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.SavePosition(new Position{Client=client.Name,ClientId=client.Id,Title="Attempt",Criteria=[new(){Name="Experience",Weight=100}]}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.TalentCandidates());
        await using(var db=f.Factory.CreateDbContext()){var u=await db.Users.SingleAsync();u.TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(1);u.CanCreatePositions=true;await db.SaveChangesAsync();}
        Assert.Equal(2,(await ats.TalentCandidates()).Count);await access.RequireTalentCandidate(pool.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.SaveCandidate(pool));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>workspace.Notes(pool.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>workspace.AddNote(candidate.Id,foreign.Id,"Attempt to attach foreign application"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.SavePosition(new Position{Client="Implicit New Client",Title="Attempt",Criteria=[new(){Name="Experience",Weight=100}]}));
        var changed=(await ats.Positions()).Single();changed.AssignedRecruiter="someone";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.SavePosition(changed));
        changed=(await ats.Positions()).Single();changed.Title="Updated Assigned Role";await ats.SavePosition(changed);
        await workspace.AddNote(candidate.Id,application.Id,"Operational note");Assert.Single(await workspace.Notes(candidate.Id));
        await using(var db=f.Factory.CreateDbContext()){
            (await db.Users.SingleAsync()).TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(-1);
            (await db.Positions.SingleAsync(p=>p.Id==owned.Id)).AssignedRecruiter="someone";await db.SaveChangesAsync();
        }
        Assert.Empty(await ats.Positions());Assert.Empty(await ats.Candidates());Assert.Empty(await ats.Assessments());Assert.Empty(await workspace.Clients());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequireApplication(application.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ats.TalentCandidates());
    }

    [Fact] public async Task RecruiterGrantsRevocationAndInactiveAccountAreEnforcedWithoutCookieRefresh()
    {
        await using var f=new Fixture();await f.Init();await using(var db=f.Factory.CreateDbContext()){db.Users.Add(new(){Id="recruiter",UserName="recruiter",Status="Active",CanExportReports=false,TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(1)});var r=new IdentityRole("Recruiter"){Id="role"};db.Roles.Add(r);db.UserRoles.Add(new(){UserId="recruiter",RoleId=r.Id});await db.SaveChangesAsync();}
        var access=new Access(new State("recruiter","Recruiter"),f.Factory);await access.RequirePermission("Talent.View");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequirePermission("Reports.Export"));
        await using(var db=f.Factory.CreateDbContext()){var u=await db.Users.SingleAsync();u.TalentPoolPaidUntilUtc=DateTime.UtcNow.AddDays(-1);u.CanViewTalentPool=true;await db.SaveChangesAsync();}await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequirePermission("Talent.View"));
        await using(var db=f.Factory.CreateDbContext()){var u=await db.Users.SingleAsync();u.Status="Inactive";await db.SaveChangesAsync();}await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.Require());
    }
    [Fact] public async Task BatchOwnershipAndNoteAuthorsCannotBeBypassed()
    {
        await using var f=new Fixture();await f.Init();var p=new Position{Reference="001",Title="Role",Client="Client",CriteriaLocked=true};var c=new Candidate{Name="Person",LegacyId="CAND-001"};await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(p);db.Candidates.Add(c);await db.SaveChangesAsync();}
        var first=new WorkspaceService(f.Factory,new Access(new State("one","Recruiter")));var second=new WorkspaceService(f.Factory,new Access(new State("two","Recruiter")));var batch=await first.CreateBatch(p.Id,["one.txt"]);Assert.Empty(await second.Batches());await Assert.ThrowsAsync<InvalidOperationException>(()=>second.SetBatchItem(batch,0,null));await Assert.ThrowsAsync<InvalidOperationException>(()=>first.CompleteBatch(batch));
        await first.AddNote(c.Id,null,"Original");var note=(await first.Notes(c.Id)).Single();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>second.EditNote(note.Id,"Changed"));await first.EditNote(note.Id,"Edited");var events=await first.Activity(c.Id);Assert.Contains(events,x=>x.Action=="Note edited"&&x.PreviousJson.Contains("Original")&&x.NewJson.Contains("Edited"));
    }
    [Fact] public async Task FailedBatchRetryKeepsOriginalItemAndNeverRepeatsSavedAssessment()
    {
        await using var f=new Fixture();await f.Init();var p=new Position{Reference="001",Title="Role",Client="Client",CriteriaLocked=true,Criteria=[new(){Name="Experience",Weight=100,Knockout=true}]};await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(p);db.DecisionRules.Add(new(){MustHaveFit="Pass",MinimumMatch=60,Decision="Shortlist"});await db.SaveChangesAsync();}
        var access=new Access(new State());var workspace=new WorkspaceService(f.Factory,access);var batch=await workspace.CreateBatch(p.Id,["test.txt"]);var provider=new Provider{FailFirst=true};var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OpenAI:ApiKey","test-key"},{"OpenAI:Model","test-model"}}).Build();var ai=new AiService(new HttpClient(provider),config,f.Factory,access,new DatabaseDocumentStore(f.Factory));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ai.Assess(p.Id,"test.txt","Evidence"u8.ToArray(),batchId:batch));await workspace.SetBatchItem(batch,0,null,"Provider unavailable");
        var draft=await ai.Assess(p.Id,"test.txt","Evidence"u8.ToArray(),batchId:batch);await workspace.SetBatchItem(batch,0,draft);var saved=await new AtsService(f.Factory,access).SaveDraft(draft);await workspace.CompleteBatch(batch);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ai.Assess(p.Id,"test.txt","Evidence"u8.ToArray(),batchId:batch));Assert.Equal(2,provider.Calls);var actual=(await workspace.Batches()).Single();Assert.Equal("Completed",actual.State);Assert.Equal(saved,actual.Items.Single().AssessmentId);Assert.Equal("Saved",actual.Items.Single().State);
    }
    [Fact] public async Task ClientContactAndCriteriaEditsPersistAndClosureHistorySurvivesReopen()
    {
        await using var f=new Fixture();await f.Init();var access=new Access(new State());var workspace=new WorkspaceService(f.Factory,access);var ats=new AtsService(f.Factory,access);var client=new Client{Name="Original Client",Contacts=[new(){Name="Original contact"}]};await workspace.SaveClient(client);
        var position=new Position{Client=client.Name,ClientId=client.Id,Title="Role",Criteria=[new(){Name="Experience",Weight=100}]};await ats.SavePosition(position);Assert.Equal("001",(await ats.Positions()).Single().Reference);
        var edit=(await ats.Positions()).Single();edit.Criteria=[new(){Name="Changed criterion",Weight=100}];await ats.SavePosition(edit);Assert.Equal("Changed criterion",(await ats.Positions()).Single().Criteria.Single().Name);
        var clientEdit=(await workspace.Clients()).Single();clientEdit.Name="Renamed Client";clientEdit.Contacts=[new(){Name="New contact"}];await workspace.SaveClient(clientEdit);Assert.Equal("New contact",(await workspace.Clients()).Single().Contacts.Single().Name);
        edit=(await ats.Positions()).Single();Assert.Equal("Renamed Client",edit.Client);edit.Status="Closed";edit.ClosureReason="Requisition paused";await ats.SavePosition(edit);edit=(await ats.Positions()).Single();var closed=edit.ClosedDate;var actor=edit.ClosedBy;edit.Status="Active";await ats.SavePosition(edit);edit=(await ats.Positions()).Single();Assert.Equal(closed,edit.ClosedDate);Assert.Equal(actor,edit.ClosedBy);Assert.Equal("Closed",edit.ClosureStatus);
    }
    [Fact] public async Task AssessmentVersionsRejectEdits()
    {
        await using var f=new Fixture();await f.Init();await using var db=f.Factory.CreateDbContext();var p=new Position{Reference="001",Title="Role",Client="Client"};db.Positions.Add(p);var a=new Assessment{PositionId=p.Id};db.Assessments.Add(a);db.AssessmentVersions.Add(DatabaseInitializer.VersionOf(a));await db.SaveChangesAsync();var version=await db.AssessmentVersions.SingleAsync();version.Decision="Changed";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }

    [Fact] public async Task RecruiterChoicesAreActiveAndRoleScoped()
    {
        await using var f=new Fixture();await f.Init();
        await using(var db=f.Factory.CreateDbContext()){
            db.Roles.Add(new IdentityRole{Id="recruiter-role",Name="Recruiter",NormalizedName="RECRUITER"});
            db.Users.AddRange(new AtsUser{Id="one",UserName="recruiter.one",FullName="One",Status="Active"},
                              new AtsUser{Id="two",UserName="recruiter.two",FullName="Two",Status="Inactive"},
                              new AtsUser{Id="other",UserName="other.user",Status="Active"});
            db.UserRoles.AddRange(new IdentityUserRole<string>{UserId="one",RoleId="recruiter-role"},new IdentityUserRole<string>{UserId="two",RoleId="recruiter-role"});
            await db.SaveChangesAsync();
        }
        var choices=await new WorkspaceService(f.Factory,new Access(new State())).Recruiters();
        Assert.Equal("recruiter.one",Assert.Single(choices).Username);
        choices=await new WorkspaceService(f.Factory,new Access(new State("one","Recruiter"))).Recruiters();
        Assert.Single(choices); Assert.DoesNotContain(choices,x=>x.Username=="recruiter.two");
    }
    [Fact] public async Task SelectedClosureDateAndClientContactPersist()
    {
        await using var f=new Fixture();await f.Init();
        var access=new Access(new State());var workspace=new WorkspaceService(f.Factory,access);var ats=new AtsService(f.Factory,access);
        var client=new Client{Name="Client",Contacts=[new(){Name="Hiring Manager"}]};await workspace.SaveClient(client);
        var p=new Position{Title="Role",Client=client.Name,ClientId=client.Id,HiringContact="Hiring Manager",Criteria=[new(){Name="Experience",Weight=100}]};
        await ats.SavePosition(p);
        var saved=(await ats.Positions()).Single();saved.Status="Closed";saved.ClosureReason="Budget Withdrawn";saved.ClosedDate=new DateTime(2026,10,1);await ats.SavePosition(saved);
        saved=(await ats.Positions()).Single();Assert.Equal(new DateTime(2026,10,1),saved.ClosedDate);Assert.Equal("Hiring Manager",saved.HiringContact);
        saved.HiringContact="Unlinked";await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(saved));
    }
    [Fact] public async Task ApprovedCriteriaCanChangeWithoutRewritingAssessmentHistory()
    {
        await using var f=new Fixture();await f.Init();var access=new Access(new State());var ats=new AtsService(f.Factory,access);
        var p=new Position{Client="Client",Title="Role",CriteriaLocked=true,Criteria=[new(){Name="Original",Weight=100}]};
        await ats.SavePosition(p);
        await using(var db=f.Factory.CreateDbContext()){
            db.Assessments.Add(new Assessment{PositionId=p.Id,State="Saved",Model="Actual provider",SnapshotJson="original snapshot",MatchPercent=73});
            await db.SaveChangesAsync();
        }
        var edit=(await ats.Positions()).Single();edit.Criteria=[new(){Name="Revised",Weight=100,Knockout=true}];await ats.SavePosition(edit);
        Assert.Equal("Revised",(await ats.Positions()).Single().Criteria.Single().Name);
        await using(var db=f.Factory.CreateDbContext()){
            var result=await db.Assessments.SingleAsync();Assert.Equal("original snapshot",result.SnapshotJson);Assert.Equal(73,result.MatchPercent);
            Assert.True(await db.Audit.AnyAsync(x=>x.Action=="Position criteria updated"));
        }
        edit=(await ats.Positions()).Single();edit.Criteria.Single().Weight=80;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ats.SavePosition(edit));
    }}
