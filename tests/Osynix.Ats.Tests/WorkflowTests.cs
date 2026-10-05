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
    sealed class Fixture:IAsyncDisposable {
        public SqliteConnection Connection=new("Data Source=:memory:");public Factory Factory=null!;
        public async Task Init(){await Connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(Connection).Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);}
        public ValueTask DisposeAsync()=>Connection.DisposeAsync();
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
    [Fact] public async Task StoredReassessmentMakesOneProviderCallAndSavePreservesMasterFacts()
    {
        await using var f=new Fixture();await f.Init();var position=new Position{Reference="001",Client="Client",Title="Role",CriteriaLocked=true,Criteria=[new(){Name="Experience",Weight=100,Knockout=true}]};var person=new Candidate{Name="Person",Email="person@example.test",LegacyId="CAND-001",Summary="Original master summary",IntelligenceJson="[{\"fact\":\"Manager since 2020\",\"source\":\"CV\"}]"};
        await using(var db=f.Factory.CreateDbContext()){db.Positions.Add(position);db.Candidates.Add(person);db.DecisionRules.Add(new(){MustHaveFit="Pass",MinimumMatch=60,Decision="Shortlist"});await db.SaveChangesAsync();}
        var provider=new Provider();var access=new Access(new State());var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OpenAI:ApiKey","test-key"},{"OpenAI:Model","test-model"}}).Build();
        var ai=new AiService(new HttpClient(provider),config,f.Factory,access,new DatabaseDocumentStore(f.Factory));var draft=await ai.AssessStored(person.Id,position.Id);Assert.Equal(1,provider.Calls);Assert.Contains("STORED VACANCY-INDEPENDENT",provider.Request);Assert.DoesNotContain("input_file",provider.Request);
        await using(var db=f.Factory.CreateDbContext()){var a=await db.Assessments.SingleAsync();var snap=JsonSerializer.Deserialize<AssessmentSnapshot>(a.SnapshotJson)!;Assert.Equal("Actual Manager",snap.Profile.CurrentRole);Assert.Equal("Actual Company",snap.Profile.CurrentCompany);Assert.Equal("2020",snap.Profile.EmploymentHistory.Single().StartDate);Assert.Equal(80,a.MatchPercent);}
        var ats=new AtsService(f.Factory,access);await ats.SaveDraft(draft);await ats.Assessments();await ats.Versions(draft);Assert.Equal(1,provider.Calls);
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
    [Fact] public async Task RecruiterGrantsRevocationAndInactiveAccountAreEnforcedWithoutCookieRefresh()
    {
        await using var f=new Fixture();await f.Init();await using(var db=f.Factory.CreateDbContext()){db.Users.Add(new(){Id="recruiter",UserName="recruiter",Status="Active",CanExportReports=false});var r=new IdentityRole("Recruiter"){Id="role"};db.Roles.Add(r);db.UserRoles.Add(new(){UserId="recruiter",RoleId=r.Id});await db.SaveChangesAsync();}
        var access=new Access(new State("recruiter","Recruiter"),f.Factory);await access.RequirePermission("Talent.View");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequirePermission("Reports.Export"));
        await using(var db=f.Factory.CreateDbContext()){var u=await db.Users.SingleAsync();u.CanViewTalentPool=false;await db.SaveChangesAsync();}await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>access.RequirePermission("Talent.View"));
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
}
