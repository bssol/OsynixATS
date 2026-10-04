using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public record WorkbookPreview(Dictionary<string,int> Rows,List<string> Warnings,string Hash);
public class WorkbookImport(IDbContextFactory<AtsDbContext> factory,Access access)
{
    static string V(Dictionary<string,string> row,params string[] names) => names.Select(n=>row.GetValueOrDefault(n,"").Trim()).FirstOrDefault(x=>x.Length>0)??"";
    static double? Number(string s) => double.TryParse(s.Replace("%","").Replace(",",""),NumberStyles.Any,CultureInfo.InvariantCulture,out var n)?n:null;
    static string Ref(string s) => int.TryParse(s,out var n)?n.ToString("000"):s.Trim();
    static bool Yes(string s) => s.Equals("yes",StringComparison.OrdinalIgnoreCase)||s.Equals("true",StringComparison.OrdinalIgnoreCase);
    static List<(int Number,Dictionary<string,string> Values)> Rows(IXLWorksheet sheet)
    {
        var rows=new List<(int,Dictionary<string,string>)>(); var last=sheet.LastRowUsed()?.RowNumber()??0; var cols=sheet.LastColumnUsed()?.ColumnNumber()??0;
        if(last>50000||cols>300) throw new InvalidOperationException("Workbook exceeds the import limit (50,000 rows or 300 columns per sheet).");
        var headers=Enumerable.Range(1,cols).Select(c=>sheet.Cell(1,c).GetString().Trim()).ToList();
        for(int r=2;r<=last;r++) {
            var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for(int c=1;c<=cols;c++) {
                var name=string.IsNullOrWhiteSpace(headers[c-1])?$"Column {c}":headers[c-1];
                if(values.ContainsKey(name)) name+=$" [column {c}]";
                values[name]=sheet.Cell(r,c).GetFormattedString(CultureInfo.InvariantCulture);
            }
            if(values.Values.Any(x=>!string.IsNullOrWhiteSpace(x))) rows.Add((r,values));
        }
        return rows;
    }
    static XLWorkbook Open(byte[] bytes)
    {
        if(bytes.Length==0||bytes.Length>25*1024*1024) throw new InvalidOperationException("Upload an XLSX workbook of up to 25 MB.");
        // Bound decompressed ZIP size before ClosedXML loads the workbook.
        using(var zip=new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
            if(zip.Entries.Sum(e=>e.Length)>150*1024*1024) throw new InvalidOperationException("Expanded workbook exceeds 150 MB.");
        return new XLWorkbook(new MemoryStream(bytes));
    }
    public async Task<WorkbookPreview> Preview(byte[] bytes)
    {
        await access.Require(true); using var wb=Open(bytes); var counts=wb.Worksheets.ToDictionary(s=>s.Name,s=>Rows(s).Count); var warnings=new List<string>();
        foreach(var required in new[]{"Positions Master","JD Criteria","Talent Pool Master","Lists & Settings"}) if(!counts.ContainsKey(required)) warnings.Add("Missing sheet: "+required);
        if(!counts.ContainsKey("Candidates")&&!counts.ContainsKey("Recruitment Pipeline")) warnings.Add("No Candidates / Recruitment Pipeline sheet found.");
        warnings.Add("Import is for an empty ATS database. Original row values are retained, but linked Google Drive CV files are not downloaded.");
        warnings.Add("Imported criteria remain unlocked until reviewed. Historical scores are preserved, not recalculated.");
        return new(counts,warnings,Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public async Task<string> Commit(byte[] bytes)
    {
        var user=await access.Require(true); var preview=await Preview(bytes); using var wb=Open(bytes);
        if(!wb.Worksheets.TryGetWorksheet("Positions Master",out _)||!wb.Worksheets.TryGetWorksheet("JD Criteria",out _)) throw new InvalidOperationException("Positions Master and JD Criteria sheets are required.");
        await using var db=await factory.CreateDbContextAsync(); await using var tx=await db.Database.BeginTransactionAsync();
        if(await db.ImportRows.AnyAsync(x=>x.FileHash==preview.Hash)) return "This exact workbook has already been imported.";
        if(await db.Positions.AnyAsync()||await db.Candidates.AnyAsync()||await db.Assessments.AnyAsync()) throw new InvalidOperationException("Import requires an empty ATS database. Use a fresh database for migration rehearsal.");
        var sheets=wb.Worksheets.ToDictionary(x=>x.Name,Rows,StringComparer.OrdinalIgnoreCase);
        foreach(var sheet in sheets) foreach(var row in sheet.Value) db.ImportRows.Add(new ImportRow{FileHash=preview.Hash,Sheet=sheet.Key,RowNumber=row.Number,Json=JsonSerializer.Serialize(row.Values)});
        var positions=new Dictionary<string,Position>();
        foreach(var (_,r) in sheets["Positions Master"]) {
            var reference=Ref(V(r,"Ref #")); if(reference.Length==0) continue;
            if(positions.ContainsKey(reference)) throw new InvalidOperationException("Duplicate position Ref # "+reference);
            var p=new Position{Reference=reference,Title=V(r,"Position","Position Title"),Client=V(r,"Client"),Location=V(r,"Location"),SalaryRange=V(r,"Salary Range","Salary / Budget"),EmploymentType=V(r,"Employment Type"),Status=V(r,"Status"),JobDescription=V(r,"Job Description","JD Text"),ClientNotes=V(r,"Client Notes","Notes")};
            if(p.Title.Length==0||p.Client.Length==0) throw new InvalidOperationException("Missing title/client for Ref # "+reference);
            if(p.Status.Length==0) p.Status="Active";
            positions.Add(reference,p); db.Positions.Add(p);
        }
        foreach(var (_,r) in sheets["JD Criteria"]) {
            var reference=Ref(V(r,"Ref #")); var name=V(r,"Criterion"); if(reference.Length==0&&name.Length==0) continue;
            if(!positions.TryGetValue(reference,out var p)) throw new InvalidOperationException("Criteria reference is not in Positions Master: "+reference);
            var weight=Number(V(r,"Weight %"))??0;
            p.Criteria.Add(new Criterion{Name=name,Weight=(int)Math.Round(weight),Knockout=Yes(V(r,"Knockout?")),EvidenceExpected=V(r,"Evidence Expected"),ScoringGuidance=V(r,"Scoring Guidance"),Notes=V(r,"Notes")});
        }
        // Invalid/missing criteria are retained for repair; assessment remains blocked until valid and locked.
        var candidates=new List<Candidate>();
        if(sheets.TryGetValue("Talent Pool Master",out var pool)) foreach(var (_,r) in pool) {
            var c=Profile(r); if(c.Name.Length==0) continue;
            if(candidates.Any(x=>x.NormalizedName==c.NormalizedName)) throw new InvalidOperationException("Duplicate candidate name in Talent Pool: "+c.Name+". Resolve it before importing.");
            candidates.Add(c); db.Candidates.Add(c);
        }
        var pipeline=sheets.GetValueOrDefault("Candidates")??sheets.GetValueOrDefault("Recruitment Pipeline")??[];
        var pairs=new HashSet<string>();
        foreach(var (_,r) in pipeline) {
            var name=V(r,"Candidate Name"); var reference=Ref(V(r,"Ref #")); if(name.Length==0) continue;
            if(!positions.TryGetValue(reference,out var position)) throw new InvalidOperationException("Candidate references unknown position: "+reference);
            if(!pairs.Add(Choices.Normalize(name)+"|"+reference)) throw new InvalidOperationException("Duplicate Candidate Name + Ref #: "+name+" / "+reference);
            var c=candidates.SingleOrDefault(x=>x.NormalizedName==Choices.Normalize(name));
            if(c is null) {c=Profile(r); candidates.Add(c); db.Candidates.Add(c);}
            var score=Number(V(r,"ATS Match %"))??0;
            // Formatted percentages are converted by stripping %. Bare 0..1 fractions are accepted as percent ratios.
            if(score>0&&score<1) score*=100;
            var decision=V(r,"Decision","ATS Decision");
            var snap=new AssessmentSnapshot(c,Number(V(r,"Relevant Industry Experience (Yrs)")),[],V(r,"Executive Assessment"),V(r,"Key Strengths"),V(r,"Key Gap / Risk"),position.Client,position.Title,reference);
            var a=new Assessment{PositionId=position.Id,CandidateId=c.Id,CandidateName=c.Name,CandidateKey=c.NormalizedName,State="Saved",SnapshotJson=JsonSerializer.Serialize(snap),MatchPercent=(int)Math.Round(score),MustHaveFit=V(r,"Must-Have Fit"),CoreRoleFit=Number(V(r,"Core Role Fit /10","Leadership & Operations Fit"))??0,EvidenceStrength=Number(V(r,"Evidence Strength /10"))??0,Decision=decision,Stage=V(r,"Recruitment Stage"),InterviewStatus=V(r,"Interview Status"),ClientStatus=V(r,"Client Status"),ExpectedSalary=V(r,"Expected Salary"),Availability=V(r,"Availability"),RecruiterNotes=V(r,"Recruiter Notes"),Model="Imported historical record"};
            if(a.Stage.Length==0) a.Stage=ScoringEngine.InitialStage(decision);
            if(a.InterviewStatus.Length==0) a.InterviewStatus="Not Scheduled"; if(a.ClientStatus.Length==0) a.ClientStatus="Not Shared";
            db.Assessments.Add(a);
        }
        if(wb.Worksheets.TryGetWorksheet("Lists & Settings",out var settings)) {
            db.DecisionRules.RemoveRange(await db.DecisionRules.ToListAsync());
            for(int r=2;r<=Math.Min(100,settings.LastRowUsed()?.RowNumber()??0);r++) {
                var fit=settings.Cell(r,13).GetString().Trim(); var decision=settings.Cell(r,15).GetString().Trim();
                if(fit is not ("Pass" or "Partial")||decision.Length==0) continue;
                var threshold=Number(settings.Cell(r,14).GetFormattedString(CultureInfo.InvariantCulture))??0;
                db.DecisionRules.Add(new DecisionRule{MustHaveFit=fit,MinimumMatch=(int)threshold,Decision=decision});
            }
        }
        AtsService.Audit(db,user.Identity!.Name!,"Workbook imported",preview.Hash,$"{positions.Count} positions; {candidates.Count} candidates; {pairs.Count} assessments");
        await db.SaveChangesAsync(); await tx.CommitAsync(); return $"Imported {positions.Count} positions, {candidates.Count} candidates and {pairs.Count} assessments. Review criteria and settings before screening.";
    }
    static Candidate Profile(Dictionary<string,string> r)
    {
        var name=V(r,"Candidate Name");
        return new Candidate{LegacyId=V(r,"Candidate ID"),Name=name,NormalizedName=Choices.Normalize(name),Email=V(r,"Email ID","Email").ToLowerInvariant(),Phone=V(r,"Mobile / Phone Number","Phone"),Location=V(r,"Location"),CurrentRole=V(r,"Current / Latest Designation","Current Designation"),CurrentCompany=V(r,"Current / Latest Company","Current Company"),TotalExperience=Number(V(r,"Total Experience (Yrs)")),Function=V(r,"Primary Function"),Specialization=V(r,"Secondary Function / Specialization"),Industry=V(r,"Industries / Domains"),Skills=V(r,"Core Skills"),Seniority=V(r,"Seniority Level"),Systems=V(r,"Systems / Tools"),Markets=V(r,"Geographic / Market Exposure"),Summary=V(r,"Professional Summary","Career Profile Summary"),Employment=V(r,"Employment / Career History"),Education=V(r,"Education"),TalentStatus=V(r,"Talent Pool Status").Length>0?V(r,"Talent Pool Status"):"Active",Notes=V(r,"Talent Pool Notes"),LegacyCvLink=V(r,"Source CV / File","CV / File Link")};
    }
}
