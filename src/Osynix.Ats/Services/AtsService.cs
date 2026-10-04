using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public class AtsService(IDbContextFactory<AtsDbContext> factory, Access access)
{
    public async Task<List<Position>> Positions()
    { await access.Require(); await using var db=await factory.CreateDbContextAsync(); return await db.Positions.Include(x=>x.Criteria).AsNoTracking().OrderBy(x=>x.Reference).ToListAsync(); }
    public async Task<List<Candidate>> Candidates()
    { await access.Require(); await using var db=await factory.CreateDbContextAsync(); return await db.Candidates.AsNoTracking().OrderBy(x=>x.Name).ToListAsync(); }
    public async Task<List<Assessment>> Assessments()
    { await access.Require(); await using var db=await factory.CreateDbContextAsync(); return await db.Assessments.Include(x=>x.Position).Include(x=>x.Candidate).AsNoTracking().OrderByDescending(x=>x.CreatedUtc).ToListAsync(); }
    public async Task<List<DecisionRule>> Rules()
    { await access.Require(); await using var db=await factory.CreateDbContextAsync(); return await db.DecisionRules.AsNoTracking().OrderByDescending(x=>x.MinimumMatch).ToListAsync(); }
    public async Task SaveRules(List<DecisionRule> rules)
    {
        var user=await access.Require(true); await using var db=await factory.CreateDbContextAsync();
        if(rules.Any(x=>x.MinimumMatch<0 || x.MinimumMatch>100 || x.MustHaveFit is not ("Pass" or "Partial") || !Choices.Decisions.Contains(x.Decision))) throw new InvalidOperationException("Invalid decision rule.");
        if(rules.GroupBy(x=>new{x.MustHaveFit,x.MinimumMatch}).Any(g=>g.Count()>1)) throw new InvalidOperationException("Duplicate rule thresholds.");
        db.DecisionRules.RemoveRange(await db.DecisionRules.ToListAsync());
        db.DecisionRules.AddRange(rules.Select(r=>new DecisionRule{MustHaveFit=r.MustHaveFit,MinimumMatch=r.MinimumMatch,Decision=r.Decision}));
        Audit(db,user.Identity!.Name!,"Decision rules updated","settings"); await db.SaveChangesAsync();
    }
    public async Task SavePosition(Position input)
    {
        var uid=await access.UserId(); ScoringEngine.ValidateCriteria(input.Criteria);
        if(string.IsNullOrWhiteSpace(input.Reference)||string.IsNullOrWhiteSpace(input.Title)||string.IsNullOrWhiteSpace(input.Client)) throw new InvalidOperationException("Reference, title and client are required.");
        if(input.Status is not ("Active" or "On Hold" or "Filled" or "Cancelled")) throw new InvalidOperationException("Invalid position status.");
        await using var db=await factory.CreateDbContextAsync();
        var p=await db.Positions.Include(x=>x.Criteria).SingleOrDefaultAsync(x=>x.Id==input.Id);
        if(await db.Positions.AnyAsync(x=>x.Id!=input.Id && x.Reference==input.Reference.Trim())) throw new InvalidOperationException("Reference already exists.");
        if(p is null && await db.Positions.AnyAsync(x=>x.Client==input.Client.Trim() && x.Title==input.Title.Trim() && x.Status=="Active")) throw new InvalidOperationException("An active position with this client and title already exists.");
        if(input.Status=="Filled") {
            if(input.HiredCandidateId is null && input.ClosureReason!="Client-filled") throw new InvalidOperationException("Choose the hired candidate or select Client-filled.");
            if(input.HiredCandidateId is not null) {
                var hired=await db.Assessments.SingleOrDefaultAsync(x=>x.PositionId==input.Id && x.CandidateId==input.HiredCandidateId && x.State=="Saved") ?? throw new InvalidOperationException("Select a saved candidate for this position.");
                hired.Stage="Hired"; hired.ClientStatus="Selected";
            }
        }
        if(input.Status=="Cancelled" && string.IsNullOrWhiteSpace(input.ClosureReason)) throw new InvalidOperationException("A cancellation reason is required.");
        if(p is null) { p=input; p.Reference=p.Reference.Trim(); db.Positions.Add(p); }
        else {
            if(p.Revision!=input.Revision) throw new InvalidOperationException("This position changed. Reload before saving.");
            var used=await db.Assessments.AnyAsync(x=>x.PositionId==p.Id && x.Model!="Imported historical record");
            var oldCriteria=JsonSerializer.Serialize(p.Criteria.Select(x=>new{x.Name,x.Weight,x.Knockout,x.EvidenceExpected,x.ScoringGuidance,x.Notes}));
            var newCriteria=JsonSerializer.Serialize(input.Criteria.Select(x=>new{x.Name,x.Weight,x.Knockout,x.EvidenceExpected,x.ScoringGuidance,x.Notes}));
            if((p.CriteriaLocked||used) && (oldCriteria!=newCriteria || p.JobDescription!=input.JobDescription || p.ClientNotes!=input.ClientNotes)) throw new InvalidOperationException("Criteria/JD are locked. Create a new position reference to change assessment requirements.");
            p.Title=input.Title.Trim(); p.Client=input.Client.Trim(); p.Location=input.Location; p.SalaryRange=input.SalaryRange; p.EmploymentType=input.EmploymentType;
            p.Status=input.Status; p.ClosureReason=input.ClosureReason; p.HiredCandidateId=input.Status=="Filled"?input.HiredCandidateId:null;
            if(!p.CriteriaLocked&&!used) {
                p.JobDescription=input.JobDescription; p.ClientNotes=input.ClientNotes;
                db.Criteria.RemoveRange(p.Criteria); p.Criteria=input.Criteria.Select(c=>new Criterion{Name=c.Name,Weight=c.Weight,Knockout=c.Knockout,EvidenceExpected=c.EvidenceExpected,ScoringGuidance=c.ScoringGuidance,Notes=c.Notes}).ToList();
            }
            p.CriteriaLocked=p.CriteriaLocked||used||input.CriteriaLocked;
        }
        Audit(db,uid,"Position saved",p.Id.ToString()); await db.SaveChangesAsync();
    }
    public async Task SaveDraft(Guid id)
    {
        var uid=await access.UserId(); await using var db=await factory.CreateDbContextAsync(); await using var tx=await db.Database.BeginTransactionAsync();
        var a=await db.Assessments.SingleAsync(x=>x.Id==id);
        if(a.State=="Saved") return;
        var snapshot=JsonSerializer.Deserialize<AssessmentSnapshot>(a.SnapshotJson) ?? throw new InvalidOperationException("Assessment snapshot is missing.");
        var incoming=snapshot.Profile; incoming.NormalizedName=Choices.Normalize(incoming.Name);
        incoming.Email=incoming.Email.Trim().ToLowerInvariant();
        var sameName=await db.Candidates.Where(x=>x.NormalizedName==incoming.NormalizedName).ToListAsync();
        var sameEmail=incoming.Email.Length>0?await db.Candidates.Where(x=>x.Email==incoming.Email).ToListAsync():[];
        var possible=sameName.Concat(sameEmail).DistinctBy(x=>x.Id).ToList();
        if(possible.Count>1) throw new InvalidOperationException("Several candidate identities match. Resolve duplicate profiles before saving.");
        var c=possible.SingleOrDefault();
        if(c is not null && c.Email.Length>0 && incoming.Email.Length>0 && c.Email!=incoming.Email) throw new InvalidOperationException("Same-name profile has a different email. Resolve identity before saving.");
        if(c is not null && await db.Assessments.AnyAsync(x=>x.CandidateId==c.Id && x.PositionId==a.PositionId)) throw new InvalidOperationException("This candidate already has an assessment for this reference. Existing data has not been overwritten.");
        if(c is null) {c=incoming; c.Id=Guid.NewGuid(); db.Candidates.Add(c);}
        // Existing profile is preserved; assessment snapshot retains the newly extracted CV version.
        c.DocumentId=a.DocumentId;
        a.CandidateId=c.Id; a.CandidateKey=incoming.NormalizedName; a.State="Saved";
        Audit(db,uid,"Assessment saved",a.Id.ToString()); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task SaveCandidate(Candidate input)
    {
        var uid=await access.UserId(); await using var db=await factory.CreateDbContextAsync();
        var c=await db.Candidates.SingleAsync(x=>x.Id==input.Id);
        if(c.Revision!=input.Revision) throw new InvalidOperationException("Profile changed. Reload before saving.");
        if(string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Candidate name is required.");
        input.NormalizedName=Choices.Normalize(input.Name); input.Email=input.Email.Trim().ToLowerInvariant();
        db.Entry(c).CurrentValues.SetValues(input); Audit(db,uid,"Candidate profile updated",c.Id.ToString()); await db.SaveChangesAsync();
    }
    public async Task SaveLifecycle(Assessment input)
    {
        var uid=await access.UserId(); await using var db=await factory.CreateDbContextAsync(); var a=await db.Assessments.SingleAsync(x=>x.Id==input.Id);
        if(a.State!="Saved") throw new InvalidOperationException("Save the assessment to ATS first.");
        if(a.Revision!=input.Revision) throw new InvalidOperationException("This record changed. Reload before saving.");
        if(!Choices.Stages.Contains(input.Stage)||!Choices.Interviews.Contains(input.InterviewStatus)||!Choices.ClientStatuses.Contains(input.ClientStatus)) throw new InvalidOperationException("Invalid lifecycle selection.");
        a.Stage=input.Stage; a.InterviewStatus=input.InterviewStatus; a.ClientStatus=input.ClientStatus;
        if(a.Stage=="Interview Scheduled") a.InterviewStatus="Scheduled";
        if(a.Stage=="Interviewed") a.InterviewStatus="Completed";
        if(a.Stage=="Hired") a.ClientStatus="Selected";
        a.ExpectedSalary=input.ExpectedSalary; a.CurrentCompensation=input.CurrentCompensation; a.Availability=input.Availability; a.NoticePeriod=input.NoticePeriod; a.RecruiterNotes=input.RecruiterNotes;
        Audit(db,uid,"Lifecycle updated",a.Id.ToString(),a.Stage); await db.SaveChangesAsync();
    }
    public static void Audit(AtsDbContext db,string user,string action,string entity,string detail="") => db.Audit.Add(new AuditEntry{UserId=user,Action=action,EntityId=entity,Detail=detail});
}
