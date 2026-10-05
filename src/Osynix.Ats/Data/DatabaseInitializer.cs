using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Data;

public static class DatabaseInitializer
{
    public const string LegacyMigration = "20261005061539_InitialLegacySchema";
    static readonly SemaphoreSlim Gate = new(1,1);

    public static async Task InitializeAsync(AtsDbContext db, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try {
            await db.Database.OpenConnectionAsync(token);
            var connection=(SqliteConnection)db.Database.GetDbConnection();
            var tables=await Tables(connection,token);
            var baseline=tables.Any(t=>!t.StartsWith("__EF")) && !(await db.Database.GetAppliedMigrationsAsync(token)).Any();
            if (baseline) await ValidateLegacySchema(db,connection,token);
            var pending=baseline || (await db.Database.GetPendingMigrationsAsync(token)).Any();
            if (pending && tables.Count>0 && !string.IsNullOrWhiteSpace(connection.DataSource) && connection.DataSource!=":memory:") {
                var path=Path.GetFullPath(connection.DataSource);
                var folder=Path.Combine(Path.GetDirectoryName(path)!,"Backups"); Directory.CreateDirectory(folder);
                using var backup=new SqliteConnection($"Data Source={Path.Combine(folder,$"before-upgrade-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db")}");
                backup.Open(); connection.BackupDatabase(backup);
            }
            if (baseline) {
                var history=db.GetService<IHistoryRepository>();
                await using var tx=await db.Database.BeginTransactionAsync(token);
                await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(),token);
                await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(LegacyMigration,"10.0.12")),token);
                await tx.CommitAsync(token);
            }
            await db.Database.MigrateAsync(token);
            await Backfill(db,token);
        } finally { try {await db.Database.CloseConnectionAsync();} finally {Gate.Release();} }
    }

    static async Task<HashSet<string>> Tables(SqliteConnection connection,CancellationToken token)
    {
        using var cmd=connection.CreateCommand(); cmd.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        await using var reader=await cmd.ExecuteReaderAsync(token); var result=new HashSet<string>();
        while(await reader.ReadAsync(token)) result.Add(reader.GetString(0));
        return result;
    }

    static async Task ValidateLegacySchema(AtsDbContext db,SqliteConnection actual,CancellationToken token)
    {
        // Build the expected original schema from the real initial migration, not guessed table names.
        using var expected=new SqliteConnection("Data Source=:memory:"); await expected.OpenAsync(token);
        using(var create=expected.CreateCommand()) {
            create.CommandText=db.GetService<IMigrator>().GenerateScript(toMigration:LegacyMigration);
            await create.ExecuteNonQueryAsync(token);
        }
        foreach(var table in (await Tables(expected,token)).Where(t=>!t.StartsWith("__EF"))) {
            if (!(await Columns(expected,table,token)).SequenceEqual(await Columns(actual,table,token)) ||
                !(await ForeignKeys(expected,table,token)).SequenceEqual(await ForeignKeys(actual,table,token)) ||
                !(await Indexes(expected,table,token)).All((await Indexes(actual,table,token)).Contains))
                throw new InvalidOperationException($"Database table {table} does not match the recognized original ATS schema. Restore/check the database before upgrading; no baseline was recorded.");
        }
    }

    static async Task<List<string>> Columns(SqliteConnection connection,string table,CancellationToken token)
    {
        using var cmd=connection.CreateCommand(); cmd.CommandText=$"PRAGMA table_info(\"{table.Replace("\"","\"\"")}\")";
        await using var reader=await cmd.ExecuteReaderAsync(token); var result=new List<string>();
        while(await reader.ReadAsync(token)) result.Add($"{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetInt64(3)}|{reader.GetInt64(5)}");
        return result;
    }

    static async Task<List<string>> ForeignKeys(SqliteConnection connection,string table,CancellationToken token)
    {
        using var cmd=connection.CreateCommand();cmd.CommandText=$"PRAGMA foreign_key_list(\"{table.Replace("\"","\"\"")}\")";
        await using var reader=await cmd.ExecuteReaderAsync(token);var result=new List<string>();
        while(await reader.ReadAsync(token)) result.Add(string.Join('|',Enumerable.Range(1,7).Select(i=>reader.GetValue(i).ToString())));
        return result.Order().ToList();
    }
    static async Task<List<string>> Indexes(SqliteConnection connection,string table,CancellationToken token)
    {
        var indexes=new List<(string Name,long Unique,long Partial)>();
        using(var cmd=connection.CreateCommand()){cmd.CommandText=$"PRAGMA index_list(\"{table.Replace("\"","\"\"")}\")";await using var reader=await cmd.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))indexes.Add((reader.GetString(1),reader.GetInt64(2),reader.GetInt64(4)));}
        var result=new List<string>();foreach(var index in indexes){using var cmd=connection.CreateCommand();cmd.CommandText=$"PRAGMA index_info(\"{index.Name.Replace("\"","\"\"")}\")";await using var reader=await cmd.ExecuteReaderAsync(token);var cols=new List<string>();while(await reader.ReadAsync(token))cols.Add(reader.GetString(2));result.Add($"{index.Name}|{index.Unique}|{index.Partial}|{string.Join(',',cols)}");}
        return result;
    }

    static async Task Backfill(AtsDbContext db,CancellationToken token)
    {
        // Each backfill is idempotent and preserves real historical timestamps.
        var people=await db.Candidates.Include(x=>x.EmploymentHistory).Include(x=>x.EducationHistory).ToListAsync(token);
        var usedIds=people.Select(x=>x.LegacyId).ToList();
        foreach(var c in people) {
            if(c.LegacyId.Length==0) {c.LegacyId=CandidateIdentity.NextDisplayId(usedIds);usedIds.Add(c.LegacyId);}
            c.NormalizedPhone=CandidateIdentity.Phone(c.Phone); c.NormalizedName=Choices.MatchKey(c.Name);
            CandidateProfile.ReadStructuredHistory(c);
            db.EmploymentHistory.AddRange(c.EmploymentHistory.Where(x=>db.Entry(x).State==EntityState.Detached));
            db.EducationHistory.AddRange(c.EducationHistory.Where(x=>db.Entry(x).State==EntityState.Detached));
        }
        var clients=await db.Clients.ToListAsync(token);
        foreach(var p in await db.Positions.Where(x=>x.ClientId==null).ToListAsync(token)) {
            var key=Choices.MatchKey(p.Client); if(key.Length==0) continue;
            var client=clients.FirstOrDefault(x=>x.NormalizedName==key);
            if(client is null) {client=new Client{Name=p.Client,NormalizedName=key};clients.Add(client);db.Clients.Add(client);}
            p.ClientId=client.Id;
        }
        var versions=await db.AssessmentVersions.Select(x=>x.AssessmentId).Distinct().ToListAsync(token);
        var documents=await db.Documents.Select(x=>x.Id).ToListAsync(token);
        foreach(var document in await db.Documents.Where(x=>x.ContentHash=="").ToListAsync(token)) document.ContentHash=Convert.ToHexString(SHA256.HashData(document.Content));
        foreach(var a in await db.Assessments.Where(x=>x.State=="Saved").ToListAsync(token)) {
            var state=Lifecycle.UpgradeLegacy(a.Stage,a.InterviewStatus,a.ClientStatus);
            a.Stage=state.Stage;a.InterviewStatus=state.Interview;a.ClientStatus=state.Client;
            if(a.DocumentId is not null && !documents.Contains(a.DocumentId.Value)) a.DocumentId=null;
            if(!versions.Contains(a.Id)) db.AssessmentVersions.Add(VersionOf(a));
        }
        if(!await db.ReferenceOptions.AnyAsync(token)) {
            var lists=new Dictionary<string,string[]> { ["Recruitment Stage"]=Choices.Stages,["Interview Status"]=Choices.Interviews,["Client Status"]=Choices.ClientStatuses,["Position Status"]=Choices.PositionStatuses,["Talent Pool Status"]=Choices.TalentStatuses,["Priority"]=Choices.Priorities,["Filled Source"]=Choices.FilledSources };
            foreach(var (list,items) in lists) for(var i=0;i<items.Length;i++) db.ReferenceOptions.Add(new ReferenceOption{List=list,Value=items[i],Order=i});
        }
        await db.SaveImportedChangesAsync(token);
    }

    public static AssessmentVersion VersionOf(Assessment a) => new() {AssessmentId=a.Id,Number=a.VersionNumber,SnapshotJson=a.SnapshotJson,MatchPercent=a.MatchPercent,MustHaveFit=a.MustHaveFit,CoreRoleFit=a.CoreRoleFit,EvidenceStrength=a.EvidenceStrength,Decision=a.Decision,Model=a.Model,DocumentId=a.DocumentId,PolicyJson=a.PolicyJson,CreatedUtc=a.LastAssessedDate??a.DateScreened??a.CreatedUtc,UpdatedUtc=a.LastAssessedDate??a.DateScreened??a.UpdatedUtc};
}
