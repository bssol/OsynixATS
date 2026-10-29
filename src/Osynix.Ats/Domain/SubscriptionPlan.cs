namespace Osynix.Ats.Domain;

public class SubscriptionTerms
{
    public int PositionSlots { get; set; } = 1;
    public int InternalSeats { get; set; }
    public int? MonthlyAssessments { get; set; }
    public int? MonthlyJdAnalyses { get; set; }
    public int? ApplicationsPerPosition { get; set; }
    public bool Marketplace { get; set; }
    public bool MasterPool { get; set; }
    public bool MeetingIntegrations { get; set; }
    public bool Reports { get; set; }
    public bool EmailTemplates { get; set; }
    public bool HiringManagerReviews { get; set; } = true;
    public bool FreelancerDirectory { get; set; } = true;
    public int? MonthlyFreelancerApproaches { get; set; }
    public bool ZoomIntegration { get; set; } = true;
}

public class SubscriptionPlan : Record
{
    public string AccountCategory { get; set; } = "Freelancer";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Published { get; set; }
    public bool PricingConfirmed { get; set; }
    public decimal MonthlyPkr { get; set; }
    public decimal AnnualPkr { get; set; }
    public decimal MonthlyUsd { get; set; }
    public decimal AnnualUsd { get; set; }
    public decimal PromotionPercent { get; set; }
    public string PromotionLabel { get; set; } = "";
    public DateOnly? PromotionStartsOn { get; set; }
    public DateOnly? PromotionEndsOn { get; set; }
    public string TermsJson { get; set; } = "{}";
    public string Description { get; set; } = "";
}

public class SubscriptionUsage : Record
{
    public Guid AllowanceId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = "Assessment";
    public DateTime MonthUtc { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? PositionId { get; set; }
}

public class SubscriptionChange : Record
{
    public Guid WorkspaceId { get; set; }
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = "";
    public string PlanSnapshotJson { get; set; } = "";
    public string Kind { get; set; } = "Downgrade";
    public string Status { get; set; } = "Scheduled";
    public DateOnly EffectiveDate { get; set; }
    public string BillingPeriod { get; set; } = "Monthly";
    public string Currency { get; set; } = "PKR";
    public decimal? Price { get; set; }
    public DateTime? AppliedUtc { get; set; }
}
