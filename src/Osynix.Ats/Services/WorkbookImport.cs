using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
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
        var rows=new List<(int,Dictionary<string,string>)>();var last=sheet.LastRowUsed()?.RowNumber()??0;var cols=sheet.LastColumnUsed()?.ColumnNumber()??0;
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
        using(var zip=new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
            if(zip.Entries.Sum(e=>e.Length)>150*1024*1024) throw new InvalidOperationException("Expanded workbook exceeds 150 MB.");
        return new XLWorkbook(new MemoryStream(bytes));
    }
    static DateTime? Date(IXLWorksheet sheet,int row,params string[] headers)
    {
        var column=sheet.Row(1).CellsUsed().FirstOrDefault(c=>headers.Contains(c.GetString().Trim(),StringComparer.OrdinalIgnoreCase))?.Address.ColumnNumber;
        if(column is null) return null;
        var value=sheet.Cell(row,column.Value).CachedValue;
        if(value.Type==XLDataType.DateTime) return value.GetDateTime();
        if(value.Type==XLDataType.Number) {var n=value.GetNumber();return n>0&&n<2958466?DateTime.FromOADate(n):null;}
        var text=value.ToString(CultureInfo.InvariantCulture);
        return DateTime.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed)?DateTime.SpecifyKind(parsed,DateTimeKind.Unspecified):null;
    }
    static DateTime? SourceUtc(DateTime? value)=>value is null?null:TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value.Value,DateTimeKind.Unspecified),TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi"));
    public async Task<WorkbookPreview> Preview(byte[] bytes)
    {
        await access.Require(true);using var wb=Open(bytes);var counts=wb.Worksheets.ToDictionary(s=>s.Name,s=>Rows(s).Count);var warnings=new List<string>();
        foreach(var required in new[]{"Positions Master","JD Criteria","Talent Pool Master","Lists & Settings"}) if(!counts.ContainsKey(required)) warnings.Add("Missing sheet: "+required);
        if(!counts.ContainsKey("Candidates")) warnings.Add("Candidates application sheet is missing. Recruitment Pipeline is an aggregate, not a substitute for application history.");
        warnings.Add("Import requires an empty business database. Existing login accounts are preserved. Google session/trusted-device tokens are not imported.");
        warnings.Add("Source IDs, dates, structured history and stored criterion evidence are preserved. Missing historical scores stay unavailable. Linked Drive documents remain links.");
        warnings.Add("Imported criteria need review before new assessments. Historical scores are never recalculated.");
        return new(counts,warnings,Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public async Task<string> Commit(byte[] bytes)
    {
        var user=await access.Require(true);var preview=await Preview(bytes);using var wb=Open(bytes);
        if(!wb.Worksheets.TryGetWorksheet("Positions Master",out var positionSheet)||!wb.Worksheets.TryGetWorksheet("JD Criteria",out _)) throw new InvalidOperationException("Positions Master and JD Criteria sheets are required.");
        await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        if(await db.ImportRows.AnyAsync(x=>x.FileHash==preview.Hash)) return "This exact workbook has already been imported.";
        if(await db.Positions.AnyAsync()||await db.Candidates.AnyAsync()||await db.Assessments.AnyAsync()) throw new InvalidOperationException("Import requires an empty ATS business database. Rehearse in a separate database first.");
        var sheets=wb.Worksheets.ToDictionary(x=>x.Name,Rows,StringComparer.OrdinalIgnoreCase);
        foreach(var sheet in sheets.Where(x=>x.Key is not ("User Sessions" or "Trusted Devices")))
            foreach(var row in sheet.Value) db.ImportRows.Add(new ImportRow{FileHash=preview.Hash,Sheet=sheet.Key,RowNumber=row.Number,Json=JsonSerializer.Serialize(row.Values)});
        var clients=await db.Clients.ToListAsync();var positions=new Dictionary<string,Position>();
        foreach(var (row,r) in sheets["Positions Master"]) {
            var reference=Ref(V(r,"Ref #"));if(reference.Length==0) continue;
            if(positions.ContainsKey(reference)) throw new InvalidOperationException("Duplicate position Ref # "+reference);
            var name=V(r,"Client");var key=Choices.MatchKey(name);
            var client=clients.SingleOrDefault(x=>x.NormalizedName==key);
            if(client is null) {client=new Client{Name=name,NormalizedName=key};clients.Add(client);db.Clients.Add(client);}
            var p=new Position {Reference=reference,Title=V(r,"Position","Position Title"),Client=name,ClientId=client.Id,Location=V(r,"Location"),SalaryRange=V(r,"Salary Range","Salary / Budget"),EmploymentType=V(r,"Employment Type"),Status=V(r,"Status"),JobDescription=V(r,"Job Description","JD Text"),ClientNotes=V(r,"Client Notes","Notes"),Priority=V(r,"Priority"),HiringTarget=(int)(Number(V(r,"Hiring Target"))??1),HiringContact=V(r,"Hiring Manager / Contact"),AssignedRecruiter=V(r,"Assigned Recruiter"),CreatedBy=V(r,"Created By"),LastUpdatedBy=V(r,"Last Updated By"),ClosedBy=V(r,"Position Closed By"),ClosureStatus=V(r,"Closure Status"),FilledSource=V(r,"Filled Source"),ClosureReason=V(r,"Closure Reason"),LegacyJdLink=V(r,"JD / File Link"),JdHash=V(r,"JD File Hash"),OpenDate=Date(positionSheet,row,"Open Date"),ClosedDate=Date(positionSheet,row,"Position Closed Date")};
            if(p.Title.Length==0||name.Length==0) throw new InvalidOperationException("Missing title/client for Ref # "+reference);
            p.ClientNotes=V(r,"Client Notes");p.InternalNotes=V(r,"Notes");
            p.Status=p.Status.Length>0?p.Status:"Active";p.Priority=p.Priority.Length>0?p.Priority:"Medium";
            p.CreatedUtc=SourceUtc(Date(positionSheet,row,"Created Date")??p.OpenDate)??p.CreatedUtc;p.UpdatedUtc=SourceUtc(Date(positionSheet,row,"Last Updated"))??p.UpdatedUtc;
            positions.Add(reference,p);db.Positions.Add(p);
        }
        foreach(var (_,r) in sheets["JD Criteria"]) {
            var reference=Ref(V(r,"Ref #"));var name=V(r,"Criterion");if(reference.Length==0&&name.Length==0) continue;
            if(!positions.TryGetValue(reference,out var p)) throw new InvalidOperationException("Criteria reference is not in Positions Master: "+reference);
            p.Criteria.Add(new Criterion{Name=name,Weight=(int)Math.Round(Number(V(r,"Weight %"))??0),Knockout=Yes(V(r,"Knockout?")),EvidenceExpected=V(r,"Evidence Expected"),ScoringGuidance=V(r,"Scoring Guidance"),Notes=V(r,"Notes")});
        }
        var people=new List<Candidate>();var byId=new Dictionary<string,Candidate>(StringComparer.OrdinalIgnoreCase);
        if(sheets.TryGetValue("Talent Pool Master",out var pool)) foreach(var (row,r) in pool) {
            var c=Profile(r);if(c.Name.Length==0) continue;
            if(c.LegacyId.Length==0) c.LegacyId=CandidateIdentity.NextDisplayId(people.Select(x=>x.LegacyId));
            if(!byId.TryAdd(c.LegacyId,c)) throw new InvalidOperationException("Duplicate Candidate ID in Talent Pool: "+c.LegacyId);
            var source=wb.Worksheet("Talent Pool Master");c.FirstScreenedDate=Date(source,row,"First Screened Date");c.LastAssessedDate=Date(source,row,"Last Assessed Date");
            c.CreatedUtc=SourceUtc(c.FirstScreenedDate)??c.CreatedUtc;c.UpdatedUtc=SourceUtc(Date(source,row,"Last Updated"))??c.UpdatedUtc;
            CandidateProfile.ReadStructuredHistory(c);people.Add(c);db.Candidates.Add(c);
        }
        var applications=sheets.GetValueOrDefault("Candidates")??[];var pairs=new HashSet<string>();var evidenceRecords=0;
        foreach(var (row,r) in applications) {
            var name=V(r,"Candidate Name");var reference=Ref(V(r,"Ref #"));if(name.Length==0) continue;
            if(!positions.TryGetValue(reference,out var position)) throw new InvalidOperationException("Candidate references unknown position: "+reference);
            var identity=V(r,"Candidate ID");Candidate? person=null;
            if(identity.Length>0) byId.TryGetValue(identity,out person);
            if(person is null && identity.Length==0) person=CandidateIdentity.Resolve(people,Profile(r));
            if(person is null) {
                person=Profile(r);person.LegacyId=identity.Length>0?identity:CandidateIdentity.NextDisplayId(people.Select(x=>x.LegacyId));
                if(!byId.TryAdd(person.LegacyId,person)) throw new InvalidOperationException("Duplicate candidate identifier.");
                people.Add(person);db.Candidates.Add(person);
            }
            if(!pairs.Add(person.LegacyId+"|"+reference)) throw new InvalidOperationException("Duplicate Candidate ID + Ref #: "+person.LegacyId+" / "+reference);
            var score=Number(V(r,"ATS Match %"));if(score>0&&score<1) score*=100;
            var decision=V(r,"Decision","ATS Decision");var evidence=CandidateProfile.HistoricalCriteria(V(r,"Criterion Results JSON"));if(evidence.Count>0) evidenceRecords++;
            var historic=Profile(r);historic.LegacyId=person.LegacyId;
            var snap=new AssessmentSnapshot(historic,Number(V(r,"Relevant Industry Experience (Yrs)")),evidence,V(r,"Executive Assessment"),V(r,"Key Strengths"),V(r,"Key Gap / Risk"),position.Client,position.Title,reference);
            var source=wb.Worksheet("Candidates");var screened=Date(source,row,"Date Screened");var updated=Date(source,row,"Last Updated");
            var a=new Assessment{PositionId=position.Id,CandidateId=person.Id,CandidateName=name,CandidateKey=person.LegacyId,State="Saved",SnapshotJson=JsonSerializer.Serialize(snap),MatchPercent=score is null?null:(int)Math.Round(score.Value,MidpointRounding.AwayFromZero),MustHaveFit=V(r,"Must-Have Fit"),CoreRoleFit=Number(V(r,"Core Role Fit /10","Leadership & Operations Fit")),EvidenceStrength=Number(V(r,"Evidence Strength /10")),Decision=decision,Stage=V(r,"Recruitment Stage"),InterviewStatus=V(r,"Interview Status"),ClientStatus=V(r,"Client Status"),ExpectedSalary=V(r,"Expected Salary"),Availability=V(r,"Availability"),RecruiterNotes=V(r,"Recruiter Notes"),Model="Imported historical record",DateScreened=screened,LastAssessedDate=screened};
            if(a.Stage.Length==0) a.Stage=ScoringEngine.InitialStage(decision);
            a.CreatedUtc=SourceUtc(screened)??a.CreatedUtc;a.UpdatedUtc=SourceUtc(updated)??a.UpdatedUtc;
            var version=DatabaseInitializer.VersionOf(a);version.CreatedUtc=SourceUtc(screened)??a.CreatedUtc;version.UpdatedUtc=SourceUtc(screened)??a.UpdatedUtc;
            db.Assessments.Add(a);db.AssessmentVersions.Add(version);
            if(screened is not null) {
                if(person.FirstScreenedDate is null || person.FirstScreenedDate>screened) person.FirstScreenedDate=screened;
                if(person.LastAssessedDate is null || person.LastAssessedDate<screened) person.LastAssessedDate=screened;
            }
        }
        foreach(var (_,r) in sheets["Positions Master"]) {
            if(positions.TryGetValue(Ref(V(r,"Ref #")),out var p) && byId.TryGetValue(V(r,"Hired Candidate ID"),out var hired)) p.HiredCandidateId=hired.Id;
        }
        if(wb.Worksheets.TryGetWorksheet("Lists & Settings",out var settings)) {
            db.DecisionRules.RemoveRange(await db.DecisionRules.ToListAsync());
            for(int r=2;r<=Math.Min(100,settings.LastRowUsed()?.RowNumber()??0);r++) {
                var fit=settings.Cell(r,13).GetString().Trim();var decision=settings.Cell(r,15).GetString().Trim();
                if(fit is not ("Pass" or "Partial")||decision.Length==0) continue;
                db.DecisionRules.Add(new DecisionRule{MustHaveFit=fit,MinimumMatch=(int)(Number(settings.Cell(r,14).GetFormattedString(CultureInfo.InvariantCulture))??0),Decision=decision});
            }
            db.ReferenceOptions.RemoveRange(await db.ReferenceOptions.ToListAsync());
            var headerNames=new HashSet<string>{"Position Status","Recruitment Stage","Interview Status","Client Status","Decision","Priority","Talent Pool Status","Seniority Level","Filled Source"};
            foreach(var cell in settings.Row(1).CellsUsed().Where(x=>headerNames.Contains(x.GetString().Trim()))) {
                var list=cell.GetString().Trim();var seen=new HashSet<string>();var order=0;
                for(var row=2;row<=Math.Min(settings.LastRowUsed()?.RowNumber()??0,1000);row++) {
                    var value=settings.Cell(row,cell.Address.ColumnNumber).GetString().Trim();
                    if(value.Length>0 && seen.Add(value)) db.ReferenceOptions.Add(new ReferenceOption{List=list,Value=value,Order=order++});
                }
            }
        }
        if(sheets.TryGetValue("Activity Log",out var events)) foreach(var (row,r) in events) {
            var time=SourceUtc(Date(wb.Worksheet("Activity Log"),row,"Timestamp"));if(time is null) continue;
            byId.TryGetValue(V(r,"Entity ID / Ref #"),out var c);
            db.Audit.Add(new AuditEntry{UserId=V(r,"User Email"),ActorName=V(r,"User Name"),Action=V(r,"Action"),EntityType=V(r,"Entity Type"),EntityId=V(r,"Entity ID / Ref #"),CandidateId=c?.Id,Detail=V(r,"Details"),PreviousJson=V(r,"Previous Value"),NewJson=V(r,"New Value"),CreatedUtc=time.Value,UpdatedUtc=time.Value});
        }
        if(sheets.TryGetValue("Users & Access",out var accounts)) {
            var known=await db.Users.ToListAsync();var roles=await db.Roles.ToListAsync();
            foreach(var (_,r) in accounts) {
                var email=CandidateIdentity.Email(V(r,"Email"));var roleName=V(r,"Role");if(email.Length==0 || roleName is not ("Admin" or "Recruiter")) continue;
                // Existing logins are never overwritten by a workbook. Imported users require a password reset by Admin.
                if(known.Any(x=>CandidateIdentity.Email(x.Email??"")==email||x.NormalizedUserName==email.ToUpperInvariant())) continue;
                var account=new AtsUser{UserName=email,NormalizedUserName=email.ToUpperInvariant(),Email=email,NormalizedEmail=email.ToUpperInvariant(),FullName=V(r,"Full Name"),Status=V(r,"Status"),SecurityStamp=Guid.NewGuid().ToString(),LockoutEnabled=true,CanCreatePositions=Yes(V(r,"Can Create Positions")),CanViewTalentPool=Yes(V(r,"Can View Talent Pool")),CanExportReports=Yes(V(r,"Can Export Reports"))};
                account.AssignedClients=V(r,"Assigned Clients");account.AssignedReferences=V(r,"Assigned Ref #s");
                if(account.Status.Length==0) account.Status="Pending";
                db.Users.Add(account);known.Add(account);
                var role=roles.FirstOrDefault(x=>x.Name==roleName);
                if(role is null) {role=new IdentityRole(roleName){NormalizedName=roleName.ToUpperInvariant()};roles.Add(role);db.Roles.Add(role);}
                db.UserRoles.Add(new IdentityUserRole<string>{UserId=account.Id,RoleId=role.Id});
            }
        }
        AtsService.Audit(db,user.Identity!.Name!,"Workbook imported",preview.Hash,$"{positions.Count} positions; {people.Count} people; {pairs.Count} applications; {evidenceRecords} detailed evidence records");
        await db.SaveImportedChangesAsync();await tx.CommitAsync();
        return $"Imported {positions.Count} positions, {people.Count} candidates, {pairs.Count} applications and {evidenceRecords} detailed evidence records. Review criteria and settings before screening.";
    }
    static Candidate Profile(Dictionary<string,string> r)
    {
        var name=V(r,"Candidate Name");var phone=V(r,"Mobile / Phone Number","Phone");
        return new Candidate{LegacyId=V(r,"Candidate ID"),Name=name,NormalizedName=Choices.MatchKey(name),Email=CandidateIdentity.Email(V(r,"Email ID","Email")),Phone=phone,NormalizedPhone=CandidateIdentity.Phone(phone),Location=V(r,"Location"),CurrentRole=V(r,"Current / Latest Designation","Current Designation"),CurrentCompany=V(r,"Current / Latest Company","Current Company"),TotalExperience=Number(V(r,"Total Experience (Yrs)")),Function=V(r,"Primary Function"),Specialization=V(r,"Secondary Function / Specialization"),Industry=V(r,"Industries / Domains"),Skills=V(r,"Core Skills"),Seniority=V(r,"Seniority Level"),Systems=V(r,"Systems / Tools"),Markets=V(r,"Geographic / Market Exposure"),Summary=V(r,"Professional Summary"),CareerSummary=V(r,"Career Profile Summary"),Employment=V(r,"Employment / Career History"),Education=V(r,"Education"),TalentStatus=V(r,"Talent Pool Status").Length>0?V(r,"Talent Pool Status"):"Active",Notes=V(r,"Talent Pool Notes"),LegacyCvLink=V(r,"Source CV / File","CV / File Link")};
    }
}
