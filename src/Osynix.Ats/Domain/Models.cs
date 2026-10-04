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
}
public class Candidate : Record
{
    public string LegacyId { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
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
    public string Employment { get; set; } = "";
    public string Education { get; set; } = "";
    public string TalentStatus { get; set; } = "Active";
    public string Notes { get; set; } = "";
    public string LegacyCvLink { get; set; } = "";
    public Guid? DocumentId { get; set; }
}
public class Assessment : Record
{
    public Guid PositionId { get; set; }
    public Position? Position { get; set; }
    public Guid? CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public Guid DocumentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string? CandidateKey { get; set; }
    public string State { get; set; } = "Draft";
    public string SnapshotJson { get; set; } = "";
    public int MatchPercent { get; set; }
    public string MustHaveFit { get; set; } = "";
    public double CoreRoleFit { get; set; }
    public double EvidenceStrength { get; set; }
    public string Decision { get; set; } = "";
    public string Stage { get; set; } = "Screened";
    public string InterviewStatus { get; set; } = "Not Scheduled";
    public string ClientStatus { get; set; } = "Not Shared";
    public string ExpectedSalary { get; set; } = "";
    public string CurrentCompensation { get; set; } = "";
    public string Availability { get; set; } = "";
    public string NoticePeriod { get; set; } = "";
    public string RecruiterNotes { get; set; } = "";
    public string Model { get; set; } = "";
}
public class StoredDocument : Record
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
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
}
public class ImportRow : Record
{
    public string FileHash { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int RowNumber { get; set; }
    public string Json { get; set; } = "";
}
public record Evidence(string Criterion, int Score, int Confidence, string Found, string Rationale);
public record ScoredCriterion(string Name, int Weight, bool Knockout, int Score, int Confidence, string Status, string Found, string Rationale);
public record AssessmentSnapshot(Candidate Profile, double? RelevantExperience, List<ScoredCriterion> Criteria, string Summary, string Strengths, string Gaps, string Client, string PositionTitle, string Reference);
public record ScoringResult(int Match, string MustHave, double Core, double Confidence, string Decision, List<ScoredCriterion> Criteria);
public static class Choices
{
    public static readonly string[] Stages = ["Screened", "Shortlisted", "Hold", "Rejected", "Interview Scheduled", "Interviewed", "Submitted to Client", "Background Check", "Offer", "Hired", "Withdrawn"];
    public static readonly string[] Decisions = ["Priority Shortlist", "Shortlist", "Consider", "Hold", "Reject", "Insufficient Evidence"];
    public static readonly string[] Interviews = ["Not Scheduled", "Scheduled", "Completed", "No Show", "Cancelled"];
    public static readonly string[] ClientStatuses = ["Not Shared", "Shared", "Under Review", "Selected", "Rejected", "On Hold"];
    public static string Tone(string decision) => decision.Contains("Shortlist", StringComparison.OrdinalIgnoreCase) ? "green" : decision is "Consider" or "Partial" ? "yellow" : "red";
    public static string Normalize(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
