using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Data;
public class AtsUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string Status { get; set; } = "Active";
    public bool CanCreatePositions { get; set; } = true;
    public bool CanViewTalentPool { get; set; } = true;
    public bool CanExportReports { get; set; } = true;
    public string AssignedClients { get; set; } = "";
    public string AssignedReferences { get; set; } = "";
}
public class AtsDbContext(DbContextOptions<AtsDbContext> options) : IdentityDbContext<AtsUser>(options)
{
    bool preserveTimestamps;
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
        b.Entity<Position>().HasIndex(x=>x.Reference).IsUnique();
        b.Entity<Position>().HasMany(x=>x.Criteria).WithOne().HasForeignKey(x=>x.PositionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Position>().HasOne(x=>x.ClientRecord).WithMany().HasForeignKey(x=>x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().HasOne<StoredDocument>().WithMany().HasForeignKey(x=>x.JdDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Client>().HasIndex(x=>x.NormalizedName).IsUnique();
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
