using System.ComponentModel.DataAnnotations;

namespace Osynix.Ats.Domain;

public abstract class Record
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [ConcurrencyCheck] public Guid Revision { get; set; } = Guid.NewGuid();
}
public class Position : Record
{
    [Required, MaxLength(30)] public string Reference { get; set; } = "";
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required, MaxLength(200)] public string Client { get; set; } = "";
    public Guid? ClientId { get; set; }
    public Client? ClientRecord { get; set; }
    public string Priority { get; set; } = "Medium";
    public int HiringTarget { get; set; } = 1;
    public DateTime? OpenDate { get; set; }
    public string HiringContact { get; set; } = "";
    public string AssignedRecruiter { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string LastUpdatedBy { get; set; } = "";
    public string ClosedBy { get; set; } = "";
    public DateTime? ClosedDate { get; set; }
    public string ClosureStatus { get; set; } = "";
    public string FilledSource { get; set; } = "";
    public string LegacyJdLink { get; set; } = "";
    public string JdHash { get; set; } = "";
    public Guid? JdDocumentId { get; set; }
    public string PositionSummary { get; set; } = "";
    public string InternalNotes { get; set; } = "";
    public string Location { get; set; } = "";
    public string SalaryRange { get; set; } = "";
    public string EmploymentType { get; set; } = "Full-time";
    public string Status { get; set; } = "Active";
    public string JobDescription { get; set; } = "";
    public string ClientNotes { get; set; } = "";
    public string ClosureReason { get; set; } = "";
    public Guid? HiredCandidateId { get; set; }
    public bool CriteriaLocked { get; set; }
    public List<Criterion> Criteria { get; set; } = [];
}
public class Criterion : Record
{
    public Guid PositionId { get; set; }
    [Required] public string Name { get; set; } = "";
    [Range(1,100)] public int Weight { get; set; }
    public bool Knockout { get; set; }
    public string EvidenceExpected { get; set; } = "";
    public string ScoringGuidance { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Rationale { get; set; } = "";
}
public class Candidate : Record
{
    public string LegacyId { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string NormalizedPhone { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentRole { get; set; } = "";
    public string CurrentCompany { get; set; } = "";
    public double? TotalExperience { get; set; }
    public string Function { get; set; } = "";
    public string Specialization { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Skills { get; set; } = "";
    public string Seniority { get; set; } = "";
    public string Systems { get; set; } = "";
    public string Markets { get; set; } = "";
    public string Summary { get; set; } = "";
    public string CareerSummary { get; set; } = "";
    public string IntelligenceJson { get; set; } = "";
    public DateTime? FirstScreenedDate { get; set; }
    public DateTime? LastAssessedDate { get; set; }
    public string Employment { get; set; } = "";
    public string Education { get; set; } = "";
    public List<CandidateEmployment> EmploymentHistory { get; set; } = [];
    public List<CandidateEducation> EducationHistory { get; set; } = [];
    public string TalentStatus { get; set; } = "Active";
    public string Notes { get; set; } = "";
    public string LegacyCvLink { get; set; } = "";
    public Guid? DocumentId { get; set; }
    public StoredDocument? Document { get; set; }
}
public class Assessment : Record
{
    public Guid PositionId { get; set; }
    public Position? Position { get; set; }
    public Guid? CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public Guid? DocumentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string? CandidateKey { get; set; }
    public string State { get; set; } = "Draft";
    public string SnapshotJson { get; set; } = "";
    public int? MatchPercent { get; set; }
    public string MustHaveFit { get; set; } = "";
    public double? CoreRoleFit { get; set; }
    public double? EvidenceStrength { get; set; }
    public string Decision { get; set; } = "";
    public string Stage { get; set; } = "HR Screening";
    public string InterviewStatus { get; set; } = "";
    public string ClientStatus { get; set; } = "";
    public string ExpectedSalary { get; set; } = "";
    public string CurrentCompensation { get; set; } = "";
    public string Availability { get; set; } = "";
    public string NoticePeriod { get; set; } = "";
    public string RecruiterNotes { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime? DateScreened { get; set; }
    public DateTime? LastAssessedDate { get; set; }
    public string PolicyJson { get; set; } = "";
    public int VersionNumber { get; set; } = 1;
    public Guid? ReplacedByAssessmentId { get; set; }
    public Guid? BatchId { get; set; }
    public int BatchIndex { get; set; }
    public string SourceFileName { get; set; } = "";
    public Guid? SourceCandidateId { get; set; }
    public bool RefreshProfile { get; set; }
}
public class StoredDocument : Record
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string ContentHash { get; set; } = "";
}
public class DecisionRule : Record
{
    public string MustHaveFit { get; set; } = "Pass";
    [Range(0,100)] public int MinimumMatch { get; set; }
    public string Decision { get; set; } = "Hold";
}
public class AuditEntry : Record
{
    public string UserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Detail { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string ActorName { get; set; } = "";
    public Guid? CandidateId { get; set; }
    public Guid? AssessmentId { get; set; }
    public string PreviousJson { get; set; } = "";
    public string NewJson { get; set; } = "";
}
public class ImportRow : Record
{
    public string FileHash { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int RowNumber { get; set; }
    public string Json { get; set; } = "";
}
public record Evidence(string Criterion, int Score, int Confidence, string Found, string Rationale);
public record ScoredCriterion(string Name, int Weight, bool Knockout, int? Score, int? Confidence, string Status, string Found, string Rationale);
public record AssessmentSnapshot(Candidate Profile, double? RelevantExperience, List<ScoredCriterion> Criteria, string Summary, string Strengths, string Gaps, string Client, string PositionTitle, string Reference);
public record ScoringResult(int Match, string MustHave, double Core, double Confidence, string Decision, List<ScoredCriterion> Criteria);
public static class Choices
{
    public static readonly string[] PositionStatuses = ["Active", "On Hold", "Closed", "Filled", "Cancelled"];
    public static readonly string[] Priorities = ["High", "Medium", "Low"];
    public static readonly string[] Stages = ["Applied", "HR Screening", "Background Check", "Consider", "Shortlisted", "Technical Interview", "Final Interview", "Client Interview", "Offer Sent", "Hired", "Hold", "Rejected"];
    public static readonly string[] Decisions = ["Priority Shortlist", "Shortlist", "Consider", "Hold", "Reject", "Insufficient Evidence"];
    public static readonly string[] Interviews = ["Not Scheduled", "Scheduled", "Completed", "No Show", "Reschedule Required", "Not Required"];
    public static readonly string[] ClientStatuses = ["Not Submitted", "Submitted", "Under Review", "Client Shortlisted", "Client Rejected", "Client Interview", "Offer", "Hired"];
    public static readonly string[] TalentStatuses = ["Active", "Priority Talent", "Passive", "Placed", "Archived", "Do Not Contact"];
    public static readonly string[] FilledSources = ["Osynix Recruiter", "Client Direct Hire", "Other"];
    public static string Tone(string decision) => decision.Contains("Shortlist", StringComparison.OrdinalIgnoreCase) || decision is "Hired" or "Placed" ? "green" : decision is "Consider" or "Partial" or "Hold" or "Pending Decision" or "In Interview" ? "yellow" : "red";
    public static string Normalize(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    public static string MatchKey(string value) => System.Text.RegularExpressions.Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
}

public class Client : Record
{
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Website { get; set; } = "";
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "Active";
    public List<ClientContact> Contacts { get; set; } = [];
}
public class ClientContact : Record
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
}
public class CandidateEmployment : Record
{
    public Guid CandidateId { get; set; }
    public int SortOrder { get; set; }
    public string CompanyName { get; set; } = "";
    public string Designation { get; set; } = "";
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Location { get; set; }
}
public class CandidateEducation : Record
{
    public Guid CandidateId { get; set; }
    public int SortOrder { get; set; }
    public string Qualification { get; set; } = "";
    public string? Institution { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Location { get; set; }
}
public class AssessmentVersion : Record
{
    public Guid AssessmentId { get; set; }
    public int Number { get; set; }
    public string SnapshotJson { get; set; } = "";
    public int? MatchPercent { get; set; }
    public string MustHaveFit { get; set; } = "";
    public double? CoreRoleFit { get; set; }
    public double? EvidenceStrength { get; set; }
    public string Decision { get; set; } = "";
    public string Model { get; set; } = "";
    public string PolicyJson { get; set; } = "";
    public Guid? DocumentId { get; set; }
}
public class CandidateNote : Record
{
    public Guid CandidateId { get; set; }
    public Guid? AssessmentId { get; set; }
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
}
public class AssessmentBatch : Record
{
    public Guid? SourceCandidateId { get; set; }
    public bool RefreshProfile { get; set; }
    public Guid PositionId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Mode { get; set; } = "Single";
    public string State { get; set; } = "Open";
    public List<BatchItem> Items { get; set; } = [];
}
public class BatchItem : Record
{
    public Guid BatchId { get; set; }
    public int Index { get; set; }
    public string FileName { get; set; } = "";
    public string State { get; set; } = "Pending";
    public string Error { get; set; } = "";
    public Guid? AssessmentId { get; set; }
}
public class ReferenceOption : Record
{
    public string List { get; set; } = "";
    public string Value { get; set; } = "";
    public int Order { get; set; }
}
