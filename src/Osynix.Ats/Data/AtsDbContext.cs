using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Data;
public class AtsUser : IdentityUser { }
public class AtsDbContext(DbContextOptions<AtsDbContext> options) : IdentityDbContext<AtsUser>(options)
{
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Criterion> Criteria => Set<Criterion>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<StoredDocument> Documents => Set<StoredDocument>();
    public DbSet<DecisionRule> DecisionRules => Set<DecisionRule>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Position>().HasIndex(x=>x.Reference).IsUnique();
        b.Entity<Position>().HasMany(x=>x.Criteria).WithOne().HasForeignKey(x=>x.PositionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Candidate>().HasIndex(x=>x.NormalizedName);
        b.Entity<Candidate>().HasIndex(x=>x.Email);
        b.Entity<Assessment>().HasOne(x=>x.Position).WithMany().HasForeignKey(x=>x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().HasOne(x=>x.Candidate).WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        // Saved assessments are one record per person and vacancy; SQLite permits multiple NULL candidate IDs for drafts.
        b.Entity<Assessment>().HasIndex(x=>new{x.CandidateId,x.PositionId}).IsUnique();
        b.Entity<Assessment>().HasIndex(x=>new{x.CandidateKey,x.PositionId}).IsUnique();
        b.Entity<ImportRow>().HasIndex(x=>new{x.FileHash,x.Sheet,x.RowNumber}).IsUnique();
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach(var entry in ChangeTracker.Entries<Record>().Where(x=>x.State == EntityState.Modified))
        { entry.Entity.UpdatedUtc=DateTime.UtcNow; entry.Entity.Revision=Guid.NewGuid(); }
        return base.SaveChangesAsync(cancellationToken);
    }
}
