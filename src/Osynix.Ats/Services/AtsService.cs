using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public class AtsService(IDbContextFactory<AtsDbContext> factory, Access access)
{
    public async Task<List<Position>> Positions()
    {
        await access.Require(); await using var db=await factory.CreateDbContextAsync();
        return await db.Positions.Include(x=>x.Criteria).AsNoTracking().OrderBy(x=>x.Reference).ToListAsync();
    }
    public async Task<List<Candidate>> Candidates()
    {
        await access.Require(); await using var db=await factory.CreateDbContextAsync();
        return await db.Candidates.Include(x=>x.EmploymentHistory).Include(x=>x.EducationHistory).AsNoTracking().OrderBy(x=>x.Name).ToListAsync();
    }
    public async Task<List<Assessment>> Assessments()
    {
        var user=await access.Require(); var uid=await access.UserId(); await using var db=await factory.CreateDbContextAsync();
        return await db.Assessments.Include(x=>x.Position).Include(x=>x.Candidate).AsNoTracking()
            .Where(x=>x.State=="Saved" || user.IsInRole("Admin") || x.OwnerId==uid).OrderByDescending(x=>x.LastAssessedDate??x.CreatedUtc).ToListAsync();
    }
    public async Task<List<DecisionRule>> Rules()
    {
        await access.Require(); await using var db=await factory.CreateDbContextAsync();
        return await db.DecisionRules.AsNoTracking().OrderByDescending(x=>x.MinimumMatch).ToListAsync();
    }
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
        var uid=await access.UserId(); var actor=(await access.Require()).Identity!.Name!;
        ScoringEngine.ValidateCriteria(input.Criteria);
        if(string.IsNullOrWhiteSpace(input.Title)||string.IsNullOrWhiteSpace(input.Client)) throw new InvalidOperationException("Title and client are required.");
        if(!Choices.PositionStatuses.Contains(input.Status)||!Choices.Priorities.Contains(input.Priority)||input.HiringTarget<1) throw new InvalidOperationException("Choose a valid status, priority and hiring target of at least one.");
        await using var db=await factory.CreateDbContextAsync(); await using var tx=await db.Database.BeginTransactionAsync();
        var p=await db.Positions.Include(x=>x.Criteria).SingleOrDefaultAsync(x=>x.Id==input.Id);
        if(p is null) await access.RequirePermission("Positions.Create");
        else if(p.Revision!=input.Revision) throw new InvalidOperationException("This position changed. Reload before saving.");
        var clients=await db.Clients.ToListAsync();
        var client=input.ClientId is not null ? clients.SingleOrDefault(x=>x.Id==input.ClientId) : clients.SingleOrDefault(x=>x.NormalizedName==Choices.MatchKey(input.Client));
        if(input.ClientId is not null && client is null) throw new InvalidOperationException("Client not found.");
        if(client is null) {client=new Client{Name=input.Client.Trim(),NormalizedName=Choices.MatchKey(input.Client)};db.Clients.Add(client);}
        if(client.Status!="Active" && p is null) throw new InvalidOperationException("Choose an active client.");
        input.ClientId=client.Id; input.Client=client.Name;
        if(string.IsNullOrWhiteSpace(input.Reference)) {
            var refs=await db.Positions.Select(x=>x.Reference).ToListAsync();
            input.Reference=(refs.Select(x=>int.TryParse(x,out var n)?n:0).DefaultIfEmpty().Max()+1).ToString("000");
        }
        input.Reference=input.Reference.Trim();
        if(await db.Positions.AnyAsync(x=>x.Id!=input.Id && x.Reference==input.Reference)) throw new InvalidOperationException("Reference already exists.");
        if(p is null) {
            var sameClient=await db.Positions.Where(x=>x.ClientId==client.Id).ToListAsync();
            if(sameClient.Any(x=>(x.Status=="Active" && Choices.MatchKey(x.Title)==Choices.MatchKey(input.Title)) || (input.JdHash.Length>0 && x.JdHash==input.JdHash))) throw new InvalidOperationException("This client already has the same active position or JD document.");
        }
        var previous=p is null ? "" : JsonSerializer.Serialize(new{p.Status,p.Priority,p.HiringTarget});
        if(input.Status=="Filled") {
            if(!Choices.FilledSources.Contains(input.FilledSource)) throw new InvalidOperationException("Choose how this position was filled.");
            if(input.FilledSource=="Osynix Recruiter" && input.HiredCandidateId is null) throw new InvalidOperationException("Choose the hired candidate.");
            if(input.HiredCandidateId is not null) {
                var hired=await db.Assessments.SingleOrDefaultAsync(x=>x.PositionId==input.Id && x.CandidateId==input.HiredCandidateId && x.State=="Saved") ?? throw new InvalidOperationException("Select a saved candidate for this position.");
                var old=JsonSerializer.Serialize(new{hired.Stage,hired.ClientStatus});
                hired.Stage="Hired"; hired.ClientStatus="Hired";
                Audit(db,uid,"Application hired",hired.Id.ToString(),"",hired.CandidateId,hired.Id,old,JsonSerializer.Serialize(new{hired.Stage,hired.ClientStatus}));
            }
        }
        if(input.Status is "Closed" or "Cancelled" && string.IsNullOrWhiteSpace(input.ClosureReason)) throw new InvalidOperationException("A closure/cancellation reason is required.");
        if(p is null) {
            p=input; p.Title=p.Title.Trim(); p.CreatedBy=actor; p.OpenDate??=DateTime.UtcNow.Date; db.Positions.Add(p);
        } else {
            var used=await db.Assessments.AnyAsync(x=>x.PositionId==p.Id && x.Model!="Imported historical record");
            static string Framework(Position position) => JsonSerializer.Serialize(position.Criteria.Select(x=>new{x.Name,x.Weight,x.Knockout,x.EvidenceExpected,x.ScoringGuidance,x.Notes,x.Rationale}));
            if((p.CriteriaLocked||used) && (Framework(p)!=Framework(input)||p.JobDescription!=input.JobDescription||p.ClientNotes!=input.ClientNotes)) throw new InvalidOperationException("Criteria and JD are locked. Use a new position reference for changed requirements.");
            p.Title=input.Title.Trim();p.Client=input.Client;p.ClientId=input.ClientId;p.Location=input.Location;p.SalaryRange=input.SalaryRange;p.EmploymentType=input.EmploymentType;
            p.Priority=input.Priority;p.HiringTarget=input.HiringTarget;p.HiringContact=input.HiringContact;p.AssignedRecruiter=input.AssignedRecruiter;p.InternalNotes=input.InternalNotes;
            p.Status=input.Status;p.ClosureReason=input.ClosureReason;p.FilledSource=input.FilledSource;p.HiredCandidateId=input.Status=="Filled"?input.HiredCandidateId:null;
            if(!p.CriteriaLocked&&!used) {
                p.JobDescription=input.JobDescription;p.ClientNotes=input.ClientNotes;p.PositionSummary=input.PositionSummary;p.JdDocumentId=input.JdDocumentId;p.JdHash=input.JdHash;
                db.Criteria.RemoveRange(p.Criteria); p.Criteria=input.Criteria.Select(c=>new Criterion{Name=c.Name,Weight=c.Weight,Knockout=c.Knockout,EvidenceExpected=c.EvidenceExpected,ScoringGuidance=c.ScoringGuidance,Notes=c.Notes,Rationale=c.Rationale}).ToList();
                db.Criteria.AddRange(p.Criteria);
            }
            p.CriteriaLocked=p.CriteriaLocked||used||input.CriteriaLocked;
        }
        p.LastUpdatedBy=actor;
        if(p.Status is "Closed" or "Filled" or "Cancelled") {p.ClosedBy=p.ClosedBy.Length>0?p.ClosedBy:actor;p.ClosedDate??=DateTime.UtcNow;p.ClosureStatus=p.Status;}
        Audit(db,uid,"Position saved",p.Id.ToString(),p.Reference,null,null,previous,JsonSerializer.Serialize(new{p.Status,p.Priority,p.HiringTarget}));
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task<Guid> SaveDraft(Guid id)
    {
        var user=await access.Require(); var uid=await access.UserId();
        await using var db=await factory.CreateDbContextAsync(); await using var tx=await db.Database.BeginTransactionAsync();
        var draft=await db.Assessments.SingleAsync(x=>x.Id==id);
        if(draft.State=="Saved") return draft.Id;
        if(draft.ReplacedByAssessmentId is not null) return draft.ReplacedByAssessmentId.Value;
        if(draft.State!="Draft" || (!user.IsInRole("Admin") && draft.OwnerId!=uid)) throw new UnauthorizedAccessException("This draft is not available to your account.");
        var snapshot=JsonSerializer.Deserialize<AssessmentSnapshot>(draft.SnapshotJson) ?? throw new InvalidOperationException("Assessment snapshot is missing.");
        var incoming=snapshot.Profile;draft.CandidateName=incoming.Name; incoming.NormalizedName=Choices.MatchKey(incoming.Name); incoming.Email=CandidateIdentity.Email(incoming.Email);incoming.NormalizedPhone=CandidateIdentity.Phone(incoming.Phone);
        if(string.IsNullOrWhiteSpace(incoming.Name)) throw new InvalidOperationException("Candidate name is required.");
        var people=await db.Candidates.Include(x=>x.EmploymentHistory).Include(x=>x.EducationHistory).ToListAsync();
        var person=draft.SourceCandidateId is not null ? people.SingleOrDefault(x=>x.Id==draft.SourceCandidateId) ?? throw new InvalidOperationException("The selected candidate no longer exists.") : CandidateIdentity.Resolve(people,incoming);
        if(person is null) {
            person=incoming; person.Id=Guid.NewGuid(); person.LegacyId=CandidateIdentity.NextDisplayId(people.Select(x=>x.LegacyId));
            CandidateProfile.ReadStructuredHistory(person); CandidateProfile.ReconcileCurrentEmployment(person);
            db.Candidates.Add(person);
        } else if(draft.RefreshProfile) {
            UpdateProfile(person,incoming);
            db.EmploymentHistory.RemoveRange(person.EmploymentHistory);db.EducationHistory.RemoveRange(person.EducationHistory);
            person.EmploymentHistory=[];person.EducationHistory=[];
            CandidateProfile.ReadStructuredHistory(person);CandidateProfile.ReconcileCurrentEmployment(person);
            db.EmploymentHistory.AddRange(person.EmploymentHistory);db.EducationHistory.AddRange(person.EducationHistory);
        }
        if(draft.RefreshProfile || (person.DocumentId is null && person.LegacyCvLink.Length==0)) person.DocumentId=draft.DocumentId;
        person.FirstScreenedDate??=draft.DateScreened??draft.CreatedUtc;person.LastAssessedDate=draft.LastAssessedDate??draft.CreatedUtc;
        if(person.IntelligenceJson.Length==0) person.IntelligenceJson=incoming.IntelligenceJson;
        incoming.LegacyId=person.LegacyId;draft.SnapshotJson=JsonSerializer.Serialize(snapshot);
        var current=await db.Assessments.SingleOrDefaultAsync(x=>x.CandidateId==person.Id&&x.PositionId==draft.PositionId&&x.State=="Saved");
        Assessment saved;
        if(current is null) {
            draft.CandidateId=person.Id;draft.CandidateKey=person.LegacyId;draft.State="Saved";saved=draft;
            db.AssessmentVersions.Add(DatabaseInitializer.VersionOf(draft));
        } else {
            if(!await db.AssessmentVersions.AnyAsync(x=>x.AssessmentId==current.Id)) db.AssessmentVersions.Add(DatabaseInitializer.VersionOf(current));
            current.VersionNumber++;
            current.CandidateName=incoming.Name;current.SnapshotJson=draft.SnapshotJson;current.MatchPercent=draft.MatchPercent;current.MustHaveFit=draft.MustHaveFit;current.CoreRoleFit=draft.CoreRoleFit;current.EvidenceStrength=draft.EvidenceStrength;
            current.Decision=draft.Decision;current.Model=draft.Model;current.PolicyJson=draft.PolicyJson;current.DocumentId=draft.DocumentId;current.LastAssessedDate=draft.LastAssessedDate??draft.CreatedUtc;
            db.AssessmentVersions.Add(DatabaseInitializer.VersionOf(current));
            draft.State="Superseded";draft.ReplacedByAssessmentId=current.Id;saved=current;
        }
        if(draft.BatchId is not null) {
            var item=await db.BatchItems.SingleOrDefaultAsync(x=>x.BatchId==draft.BatchId&&x.Index==draft.BatchIndex);
            if(item is not null) {item.State="Saved";item.AssessmentId=saved.Id;}
        }
        Audit(db,uid,current is null?"Assessment saved":"Assessment version saved",saved.Id.ToString(),$"Version {saved.VersionNumber}",person.Id,saved.Id);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return saved.Id;
    }
    public async Task SaveCandidate(Candidate input)
    {
        var uid=await access.UserId(); await using var db=await factory.CreateDbContextAsync();
        var c=await db.Candidates.SingleAsync(x=>x.Id==input.Id);
        if(c.Revision!=input.Revision) throw new InvalidOperationException("Profile changed. Reload before saving.");
        if(string.IsNullOrWhiteSpace(input.Name)||!Choices.TalentStatuses.Contains(input.TalentStatus)) throw new InvalidOperationException("Name and a valid talent status are required.");
        var old=JsonSerializer.Serialize(new{c.Name,c.Email,c.Phone,c.TalentStatus,c.Notes});
        UpdateProfile(c,input); c.TalentStatus=input.TalentStatus;c.Notes=input.Notes;
        Audit(db,uid,"Candidate profile updated",c.Id.ToString(),"",c.Id,null,old,JsonSerializer.Serialize(new{c.Name,c.Email,c.Phone,c.TalentStatus,c.Notes})); await db.SaveChangesAsync();
    }
    static void UpdateProfile(Candidate target,Candidate source)
    {
        target.Name=source.Name.Trim();target.NormalizedName=Choices.MatchKey(target.Name);target.Email=CandidateIdentity.Email(source.Email);target.Phone=source.Phone;target.NormalizedPhone=CandidateIdentity.Phone(source.Phone);
        target.Location=source.Location;target.CurrentRole=source.CurrentRole;target.CurrentCompany=source.CurrentCompany;target.TotalExperience=source.TotalExperience;
        target.Function=source.Function;target.Specialization=source.Specialization;target.Industry=source.Industry;target.Skills=source.Skills;target.Seniority=source.Seniority;
        target.Systems=source.Systems;target.Markets=source.Markets;target.Summary=source.Summary;target.CareerSummary=source.CareerSummary;target.Employment=source.Employment;target.Education=source.Education;
        if(source.IntelligenceJson.Length>0) target.IntelligenceJson=source.IntelligenceJson;
    }
    public async Task SaveLifecycle(Assessment input)
    {
        var uid=await access.UserId(); await using var db=await factory.CreateDbContextAsync(); var a=await db.Assessments.SingleAsync(x=>x.Id==input.Id);
        if(a.State!="Saved") throw new InvalidOperationException("Save the assessment to ATS first.");
        if(a.Revision!=input.Revision) throw new InvalidOperationException("This record changed. Reload before saving.");
        var state=Lifecycle.Synchronize(input.Stage,input.InterviewStatus,input.ClientStatus,a.ClientStatus);
        if((a.Stage=="Hired"||a.ClientStatus=="Hired") && (state.Stage!=a.Stage||state.Client!=a.ClientStatus||state.Interview!=a.InterviewStatus)) throw new InvalidOperationException("This application is hired. Its final lifecycle cannot be reopened through a progress edit.");
        var old=JsonSerializer.Serialize(new{a.Stage,a.InterviewStatus,a.ClientStatus,a.ExpectedSalary,a.Availability,a.RecruiterNotes});
        a.Stage=state.Stage;a.InterviewStatus=state.Interview;a.ClientStatus=state.Client;
        a.ExpectedSalary=input.ExpectedSalary;a.CurrentCompensation=input.CurrentCompensation;a.Availability=input.Availability;a.NoticePeriod=input.NoticePeriod;a.RecruiterNotes=input.RecruiterNotes;
        Audit(db,uid,"Lifecycle updated",a.Id.ToString(),a.Stage,a.CandidateId,a.Id,old,JsonSerializer.Serialize(new{a.Stage,a.InterviewStatus,a.ClientStatus,a.ExpectedSalary,a.Availability,a.RecruiterNotes})); await db.SaveChangesAsync();
    }
    public async Task<List<AssessmentVersion>> Versions(Guid applicationId)
    {
        await access.Require(); await using var db=await factory.CreateDbContextAsync();
        return await db.AssessmentVersions.AsNoTracking().Where(x=>x.AssessmentId==applicationId).OrderByDescending(x=>x.Number).ToListAsync();
    }
    public static void Audit(AtsDbContext db,string user,string action,string entity,string detail="",Guid? candidateId=null,Guid? assessmentId=null,string previous="",string current="") => db.Audit.Add(new AuditEntry{UserId=user,Action=action,EntityId=entity,Detail=detail,CandidateId=candidateId,AssessmentId=assessmentId,PreviousJson=previous,NewJson=current,EntityType=assessmentId is not null?"Application":candidateId is not null?"Candidate":"Workspace"});
}
