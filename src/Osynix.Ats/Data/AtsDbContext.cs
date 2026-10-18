using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Data;
public class AtsUser : IdentityUser
{
    public string AccountType {get;set;} = "Legacy";
    public Guid? EmployerWorkspaceId {get;set;}
    public string FullName { get; set; } = "";
    public byte[]? ProfileImage{get;set;}
    public string ProfileImageType{get;set;}="";
    public string Status { get; set; } = "Active";
    public bool CanCreatePositions { get; set; }
    public bool CanCreateClients { get; set; }
    public DateTime? TalentPoolPaidUntilUtc { get; set; }
    public bool CanViewTalentPool { get; set; }
    public bool CanExportReports { get; set; }
    public string AssignedClients { get; set; } = "";
    public string AssignedReferences { get; set; } = "";
}
public class AtsDbContext(DbContextOptions<AtsDbContext> options) : IdentityDbContext<AtsUser>(options)
{
    public DbSet<MarketplaceConversation> MarketplaceConversations => Set<MarketplaceConversation>();
    public DbSet<MarketplaceMessage> MarketplaceMessages => Set<MarketplaceMessage>();
    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<InterviewBooking> InterviewBookings=>Set<InterviewBooking>();
    public DbSet<InterviewDelivery> InterviewDeliveries=>Set<InterviewDelivery>();
    public DbSet<RecruiterAction> RecruiterActions=>Set<RecruiterAction>();
    public DbSet<FreelancerProfile> FreelancerProfiles=>Set<FreelancerProfile>();
    public DbSet<PositionRecruiterAssignment> PositionRecruiterAssignments=>Set<PositionRecruiterAssignment>();
    public DbSet<EmployerProfileSubmission> EmployerProfileSubmissions=>Set<EmployerProfileSubmission>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<TalentPerson> TalentPeople => Set<TalentPerson>();
    public DbSet<EmployerInvitation> EmployerInvitations => Set<EmployerInvitation>();
    bool preserveTimestamps;
    public DbSet<EmployerWorkspace> EmployerWorkspaces => Set<EmployerWorkspace>();
    public DbSet<EmployerAccessRequest> EmployerAccessRequests => Set<EmployerAccessRequest>();
    public DbSet<HiringReviewGrant> HiringReviewGrants => Set<HiringReviewGrant>();
    public DbSet<HiringSubmission> HiringSubmissions => Set<HiringSubmission>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Criterion> Criteria => Set<Criterion>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<StoredDocument> Documents => Set<StoredDocument>();
    public DbSet<DecisionRule> DecisionRules => Set<DecisionRule>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<CandidateEmployment> EmploymentHistory => Set<CandidateEmployment>();
    public DbSet<CandidateEducation> EducationHistory => Set<CandidateEducation>();
    public DbSet<AssessmentVersion> AssessmentVersions => Set<AssessmentVersion>();
    public DbSet<CandidateNote> CandidateNotes => Set<CandidateNote>();
    public DbSet<AssessmentBatch> AssessmentBatches => Set<AssessmentBatch>();
    public DbSet<BatchItem> BatchItems => Set<BatchItem>();
    public DbSet<ReferenceOption> ReferenceOptions => Set<ReferenceOption>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<MarketplaceConversation>().HasIndex(c => new { c.WorkspaceId, c.FreelancerId, c.Agency }).IsUnique();
        b.Entity<MarketplaceConversation>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MarketplaceConversation>().HasOne<AtsUser>().WithMany().HasForeignKey(c => c.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MarketplaceMessage>().HasIndex(m => new { m.ConversationId, m.CreatedUtc });
        b.Entity<MarketplaceMessage>().HasOne<MarketplaceConversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProjectInvitation>().HasIndex(i => new { i.ConversationId, i.Status });
        b.Entity<ProjectInvitation>().HasOne<MarketplaceConversation>().WithMany().HasForeignKey(i => i.ConversationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProjectInvitation>().HasOne<Position>().WithMany().HasForeignKey(i => i.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MarketplaceMessage>().HasOne<ProjectInvitation>().WithMany().HasForeignKey(m => m.InvitationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmailTemplate>().HasIndex(t=>new{t.WorkspaceId,t.Key}).IsUnique();
        b.Entity<EmailTemplate>().HasIndex(t=>t.Key).IsUnique().HasFilter("WorkspaceId IS NULL");
        b.Entity<EmailTemplate>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(t=>t.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InterviewBooking>().HasIndex(i=>i.TokenHash).IsUnique();
        b.Entity<InterviewBooking>().HasOne<Assessment>().WithMany().HasForeignKey(i=>i.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InterviewBooking>().HasOne<HiringSubmission>().WithMany().HasForeignKey(i=>i.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InterviewDelivery>().HasOne<InterviewBooking>().WithMany().HasForeignKey(i=>i.InterviewId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecruiterAction>().HasIndex(t=>t.AssessmentId).IsUnique();
        b.Entity<RecruiterAction>().HasOne<Assessment>().WithMany().HasForeignKey(t=>t.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecruiterAction>().HasOne<HiringSubmission>().WithMany().HasForeignKey(t=>t.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecruiterAction>().HasOne<AtsUser>().WithMany().HasForeignKey(t=>t.RecipientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmployerProfileSubmission>().HasIndex(s=>new{s.WorkspaceId,s.AssessmentId}).IsUnique();
        b.Entity<EmployerProfileSubmission>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(s=>s.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmployerProfileSubmission>().HasOne<Assessment>().WithMany().HasForeignKey(s=>s.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmployerProfileSubmission>().HasOne<AtsUser>().WithMany().HasForeignKey(s=>s.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FreelancerProfile>().HasIndex(p=>p.UserId).IsUnique();
        b.Entity<FreelancerProfile>().HasOne<AtsUser>().WithMany().HasForeignKey(p=>p.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PositionRecruiterAssignment>().HasIndex(p=>new{p.PositionId,p.UserId}).IsUnique();
        b.Entity<PositionRecruiterAssignment>().HasOne<Position>().WithMany().HasForeignKey(p=>p.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PositionRecruiterAssignment>().HasOne<AtsUser>().WithMany().HasForeignKey(p=>p.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkspaceMembership>().HasIndex(m=>new{m.WorkspaceId,m.UserId}).IsUnique();
        b.Entity<WorkspaceMembership>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(m=>m.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkspaceMembership>().HasOne<AtsUser>().WithMany().HasForeignKey(m=>m.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Candidate>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(c=>c.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Candidate>().HasOne<TalentPerson>().WithMany().HasForeignKey(c=>c.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TalentPerson>().HasIndex(p=>p.EmailKey);b.Entity<TalentPerson>().HasIndex(p=>p.PhoneKey);
        b.Entity<AtsUser>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(u=>u.EmployerWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(p=>p.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Client>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(c=>c.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmployerInvitation>().HasIndex(i=>new{i.WorkspaceId,i.NormalizedEmail}).IsUnique();
        b.Entity<EmployerInvitation>().HasIndex(i=>i.TokenHash).IsUnique();
        b.Entity<EmployerInvitation>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(i=>i.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmployerWorkspace>().HasIndex(x=>x.OwnerId).IsUnique();
        b.Entity<EmployerWorkspace>().HasOne<AtsUser>().WithMany().HasForeignKey(x=>x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EmployerAccessRequest>().HasOne<EmployerWorkspace>().WithMany().HasForeignKey(x=>x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HiringReviewGrant>().HasIndex(x=>x.ClientId);
        b.Entity<HiringReviewGrant>().HasIndex(x=>x.EmailKey).IsUnique();
        b.Entity<HiringReviewGrant>().HasIndex(x=>x.TokenHash).IsUnique();
        b.Entity<HiringSubmission>().HasIndex(x=>new{x.GrantId,x.AssessmentId}).IsUnique();
        b.Entity<HiringSubmission>().HasOne<HiringReviewGrant>().WithMany().HasForeignKey(x=>x.GrantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HiringSubmission>().HasOne<Assessment>().WithMany().HasForeignKey(x=>x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().HasIndex(x=>x.Reference).IsUnique();
        b.Entity<Position>().HasMany(x=>x.Criteria).WithOne().HasForeignKey(x=>x.PositionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Position>().HasOne(x=>x.ClientRecord).WithMany().HasForeignKey(x=>x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().HasOne<StoredDocument>().WithMany().HasForeignKey(x=>x.JdDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Client>().HasIndex(x=>x.NormalizedName).IsUnique().HasFilter("\"WorkspaceId\" IS NULL");
        b.Entity<Client>().HasIndex(x=>new{x.WorkspaceId,x.NormalizedName}).IsUnique();
        b.Entity<Client>().HasMany(x=>x.Contacts).WithOne().HasForeignKey(x=>x.ClientId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Candidate>().HasIndex(x=>x.NormalizedName);
        b.Entity<Candidate>().HasIndex(x=>x.Email);
        b.Entity<Candidate>().HasIndex(x=>x.NormalizedPhone);
        b.Entity<Candidate>().HasIndex(x=>x.LegacyId).IsUnique().HasFilter("\"LegacyId\" <> ''");
        b.Entity<Candidate>().HasMany(x=>x.EmploymentHistory).WithOne().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Candidate>().HasMany(x=>x.EducationHistory).WithOne().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Candidate>().HasOne(x=>x.Document).WithMany().HasForeignKey(x=>x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().HasOne(x=>x.Position).WithMany().HasForeignKey(x=>x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().HasOne(x=>x.Candidate).WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        // Saved assessments are one record per person and vacancy; SQLite permits multiple NULL candidate IDs for drafts.
        b.Entity<Assessment>().HasIndex(x=>new{x.CandidateId,x.PositionId}).IsUnique();
        b.Entity<Assessment>().HasIndex(x=>new{x.CandidateKey,x.PositionId});
        b.Entity<Assessment>().HasOne<Assessment>().WithMany().HasForeignKey(x=>x.ReplacedByAssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().HasOne<AssessmentBatch>().WithMany().HasForeignKey(x=>x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AssessmentVersion>().HasOne<Assessment>().WithMany().HasForeignKey(x=>x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AssessmentVersion>().HasIndex(x=>new{x.AssessmentId,x.Number}).IsUnique();
        b.Entity<CandidateNote>().HasOne<Candidate>().WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CandidateNote>().HasOne<Assessment>().WithMany().HasForeignKey(x=>x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AssessmentBatch>().HasMany(x=>x.Items).WithOne().HasForeignKey(x=>x.BatchId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AssessmentBatch>().HasOne<Position>().WithMany().HasForeignKey(x=>x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReferenceOption>().HasIndex(x=>new{x.List,x.Value}).IsUnique();
        b.Entity<StoredDocument>().HasIndex(x=>x.ContentHash);
        b.Entity<ImportRow>().HasIndex(x=>new{x.FileHash,x.Sheet,x.RowNumber}).IsUnique();
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if(ChangeTracker.Entries<AssessmentVersion>().Any(x=>x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Saved assessment versions are immutable. Create a new assessment version instead.");
        foreach(var entry in ChangeTracker.Entries<Record>().Where(x=>x.State == EntityState.Modified && !preserveTimestamps))
        { entry.Entity.UpdatedUtc=DateTime.UtcNow; entry.Entity.Revision=Guid.NewGuid(); }
        return base.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SaveImportedChangesAsync(CancellationToken token = default)
    {
        preserveTimestamps=true;
        try { return await SaveChangesAsync(token); }
        finally { preserveTimestamps=false; }
    }
}
