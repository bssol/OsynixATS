using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class RebuildTests
{
    sealed class State : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,"Admin"),new(ClaimTypes.NameIdentifier,"admin"),new(ClaimTypes.Role,"Admin")],"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options) : IDbContextFactory<AtsDbContext> { public AtsDbContext CreateDbContext()=>new(options); }

    [Fact] public void AllSourceScoringCasesMatchOriginalFunctions()
    {
        using var inputs=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/scoring-reference-cases.json")));
        using var audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/source-audit.json")));
        var original=audit.RootElement.GetProperty("verification").GetProperty("scoring_comparison").EnumerateArray().ToDictionary(x=>x.GetProperty("name").GetString()!);
        var rules=inputs.RootElement.GetProperty("rules").EnumerateArray().Select(x=>new DecisionRule{MustHaveFit=x.GetProperty("mustHaveFit").GetString()!,MinimumMatch=(int)x.GetProperty("minAtsMatch").GetDouble(),Decision=x.GetProperty("decision").GetString()!}).ToList();
        foreach(var c in inputs.RootElement.GetProperty("cases").EnumerateArray()) {
            var criteria=c.GetProperty("criteria").EnumerateArray().Select(x=>new Criterion{Name=x.GetProperty("criterion").GetString()!,Weight=x.GetProperty("weight").GetInt32(),Knockout=x.GetProperty("knockout").GetString()=="Yes"}).ToList();
            var evidence=c.GetProperty("evidence").EnumerateArray().Select(x=>new Evidence(x.GetProperty("criterion").GetString()!,x.GetProperty("score_10").GetInt32(),x.GetProperty("evidence_confidence_10").GetInt32(),"","")).ToList();
            var result=ScoringEngine.Calculate(criteria,evidence,rules);var expected=original[c.GetProperty("name").GetString()!].GetProperty("reference");
            Assert.Equal(expected.GetProperty("match").GetInt32(),result.Match);Assert.Equal(expected.GetProperty("mustHave").GetString(),result.MustHave);
            Assert.Equal(expected.GetProperty("core").GetDouble(),result.Core);Assert.Equal(expected.GetProperty("confidence").GetDouble(),result.Confidence);Assert.Equal(expected.GetProperty("decision").GetString(),result.Decision);
        }
    }

    [Theory]
    [InlineData("Shortlisted","Completed","Client Rejected","Rejected")]
    [InlineData("Final Interview","Completed","Offer","Pending Decision")]
    [InlineData("Technical Interview","Scheduled","","In Interview")]
    [InlineData("Hired","Completed","Client Rejected","Hired")]
    [InlineData("Consider","","","")]
    public void LifecycleQueuesRespectPriority(string stage,string interview,string client,string expected)
    { Assert.Equal(expected,Lifecycle.Classify(new Assessment{Stage=stage,InterviewStatus=interview,ClientStatus=client})); }

    [Fact] public void NewClientInterviewResetsOnceAndCompletedRoundStaysCompletedOnRepeatedSave()
    {
        var state=Lifecycle.Synchronize("Final Interview","Completed","Client Interview","");
        Assert.Equal("Client Interview",state.Stage);Assert.Equal("Not Scheduled",state.Interview);
        Assert.Equal("Completed",Lifecycle.Synchronize(state.Stage,"Completed",state.Client,state.Client).Interview);
        Assert.Equal("Offer Sent",Lifecycle.Synchronize("Shortlisted","","Offer").Stage);
    }

    [Fact] public void ContactPriorityAndDistinctNamesArePreserved()
    {
        var first=new Candidate{Name="Same Name",Email="first@example.test",Phone="0300-1234567"};
        var second=new Candidate{Name="Other Name",Email="second@example.test"};
        Assert.Same(second,CandidateIdentity.Resolve([first,second],new Candidate{Name="Same Name",Email="SECOND@example.test"}));
        Assert.Same(first,CandidateIdentity.Resolve([first,second],new Candidate{Name="Changed",Phone="03001234567 / 111"}));
        Assert.Null(CandidateIdentity.Resolve([first],new Candidate{Name="Same Name",Email="new@example.test"}));
        Assert.Throws<InvalidOperationException>(()=>CandidateIdentity.Resolve([first,second],new Candidate{Name="Whatever",Email=second.Email,Phone=first.Phone}));
    }

    [Fact] public void StructuredHistoryRetainsDatePrecisionAndLatestEmployment()
    {
        var c=new Candidate{Employment="""[{"company_name":"Prior","designation":"Analyst","start_date":"2019","end_date":"2021"},{"company_name":"Current","designation":"Manager","start_date":"2022","end_date":"Present"}]""",Education="""[{"qualification":"BBA","institution":"University","start_date":null,"end_date":"2018"}]"""};
        CandidateProfile.ReadStructuredHistory(c);CandidateProfile.ReconcileCurrentEmployment(c);
        Assert.Equal(2,c.EmploymentHistory.Count);Assert.Equal("2019",c.EmploymentHistory[0].StartDate);Assert.Equal("Current",c.CurrentCompany);Assert.Equal("Manager",c.CurrentRole);Assert.Single(c.EducationHistory);
        var legacy=new Candidate{Employment="Original career narrative"};CandidateProfile.ReadStructuredHistory(legacy);Assert.Empty(legacy.EmploymentHistory);Assert.Equal("Original career narrative",legacy.Employment);
    }

    [Fact] public async Task LegacyUpgradePreservesUserAndBusinessRecordsAndCanRunAgain()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options;
        await using var db=new AtsDbContext(options);
        using(var command=connection.CreateCommand()) {command.CommandText=db.GetService<IMigrator>().GenerateScript(toMigration:DatabaseInitializer.LegacyMigration)+"DROP TABLE __EFMigrationsHistory;";await command.ExecuteNonQueryAsync();}
        await InsertLegacy(connection,"AspNetUsers",new(){["Id"]="admin",["UserName"]="Admin",["NormalizedUserName"]="ADMIN",["PasswordHash"]="existing-password-hash"});
        var pid=Guid.NewGuid().ToString().ToUpperInvariant();var cid=Guid.NewGuid().ToString().ToUpperInvariant();var aid=Guid.NewGuid().ToString().ToUpperInvariant();var stamp=Guid.NewGuid().ToString().ToUpperInvariant();
        await InsertLegacy(connection,"Positions",new(){["Id"]=pid,["Reference"]="001",["Title"]="Role",["Client"]="Client",["Status"]="Active",["CreatedUtc"]="2026-01-01 00:00:00",["UpdatedUtc"]="2026-01-02 00:00:00",["Revision"]=stamp});
        await InsertLegacy(connection,"Candidates",new(){["Id"]=cid,["LegacyId"]="CAND-001",["Name"]="Person",["CreatedUtc"]="2026-01-01 00:00:00",["UpdatedUtc"]="2026-01-02 00:00:00",["Revision"]=stamp,["TalentStatus"]="Active"});
        await InsertLegacy(connection,"Assessments",new(){["Id"]=aid,["PositionId"]=pid,["CandidateId"]=cid,["DocumentId"]=Guid.Empty.ToString(),["State"]="Saved",["Stage"]="Interviewed",["InterviewStatus"]="Completed",["ClientStatus"]="Not Shared",["CreatedUtc"]="2026-01-01 00:00:00",["UpdatedUtc"]="2026-01-02 00:00:00",["Revision"]=stamp});
        await DatabaseInitializer.InitializeAsync(db);await DatabaseInitializer.InitializeAsync(db);
        var user=await db.Users.SingleAsync();Assert.Equal("existing-password-hash",user.PasswordHash);Assert.Equal("Active",user.Status);Assert.True(user.CanCreatePositions);
        Assert.Equal("CAND-001",(await db.Candidates.SingleAsync()).LegacyId);Assert.Single(await db.Clients.ToListAsync());
        var application=await db.Assessments.SingleAsync();Assert.Equal("Technical Interview",application.Stage);Assert.Null(application.DocumentId);Assert.Equal(new DateTime(2026,1,2),application.UpdatedUtc);
        Assert.Single(await db.AssessmentVersions.ToListAsync());Assert.Equal(db.Database.GetMigrations().Count(),(await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact] public async Task UnknownLegacySchemaDoesNotReceiveMigrationBaseline()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new AtsDbContext(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);
        using(var command=connection.CreateCommand()){command.CommandText=db.GetService<IMigrator>().GenerateScript(toMigration:DatabaseInitializer.LegacyMigration)+"DROP TABLE __EFMigrationsHistory; DROP INDEX IX_Positions_Reference;";await command.ExecuteNonQueryAsync();}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseInitializer.InitializeAsync(db));Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
    }

    static async Task InsertLegacy(SqliteConnection connection,string table,Dictionary<string,object?> supplied)
    {
        var values=new Dictionary<string,object?>();
        using(var command=connection.CreateCommand()) {
            command.CommandText=$"PRAGMA table_info(\"{table}\")";await using var reader=await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()) {var name=reader.GetString(1);if(reader.GetInt64(3)==1) values[name]=reader.GetString(2)=="INTEGER"?0:"";}
        }
        foreach(var (key,value) in supplied) values[key]=value;
        using var insert=connection.CreateCommand();insert.CommandText=$"INSERT INTO \"{table}\" ({string.Join(',',values.Keys.Select(x=>$"\"{x}\""))}) VALUES ({string.Join(',',values.Keys.Select((_,i)=>"$p"+i))})";
        var index=0;foreach(var value in values.Values) insert.Parameters.AddWithValue("$p"+index++,value??DBNull.Value);
        await insert.ExecuteNonQueryAsync();
    }

    [Fact] public async Task ReassessmentAddsVersionPreservesLifecycleAndMasterCv()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var factory=new Factory(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);
        await using(var db=factory.CreateDbContext()) {
            await db.Database.EnsureCreatedAsync();var p=new Position{Reference="001",Title="Role",Client="Client"};db.Positions.Add(p);
            var cv=new StoredDocument{FileName="original.txt"};db.Documents.Add(cv);
            var c=new Candidate{Name="Person",Email="person@example.test",LegacyId="CAND-001",DocumentId=cv.Id};db.Candidates.Add(c);
            var snapshot=new AssessmentSnapshot(c,null,[],"Original","","","Client","Role","001");
            var a=new Assessment{PositionId=p.Id,CandidateId=c.Id,State="Saved",SnapshotJson=JsonSerializer.Serialize(snapshot),Stage="Final Interview",InterviewStatus="Completed",Decision="Hold",DocumentId=cv.Id};db.Assessments.Add(a);
            var nextCv=new StoredDocument{FileName="next.txt"};db.Documents.Add(nextCv);
            var draft=new Assessment{PositionId=p.Id,DocumentId=nextCv.Id,SnapshotJson=JsonSerializer.Serialize(snapshot),Decision="Shortlist",Stage="Shortlisted"};db.Assessments.Add(draft);
            await db.SaveChangesAsync();
        }
        Guid currentId;Guid draftId;Guid originalCv;
        await using(var db=factory.CreateDbContext()) {currentId=(await db.Assessments.SingleAsync(x=>x.State=="Saved")).Id;draftId=(await db.Assessments.SingleAsync(x=>x.State=="Draft")).Id;originalCv=(await db.Candidates.SingleAsync()).DocumentId!.Value;}
        var service=new AtsService(factory,new Access(new State()));Assert.Equal(currentId,await service.SaveDraft(draftId));Assert.Equal(currentId,await service.SaveDraft(draftId));
        await using var check=factory.CreateDbContext();var current=await check.Assessments.SingleAsync(x=>x.State=="Saved");
        Assert.Equal("Final Interview",current.Stage);Assert.Equal("Completed",current.InterviewStatus);Assert.Equal("Shortlist",current.Decision);Assert.Equal(originalCv,(await check.Candidates.SingleAsync()).DocumentId);
        var versions=await check.AssessmentVersions.OrderBy(x=>x.Number).ToListAsync();Assert.Equal(2,versions.Count);Assert.Equal("Hold",versions[0].Decision);Assert.Equal("Shortlist",versions[1].Decision);
    }
}
