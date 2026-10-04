using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Xunit;
namespace Osynix.Ats.Tests;
public class StorageTests
{
    [Fact]public async Task SamePersonSamePositionIsUniqueButDraftsAreAllowed()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options;
        await using var db=new AtsDbContext(options);await db.Database.EnsureCreatedAsync();
        var p=new Position{Reference="001",Title="Manager",Client="Client"};var c=new Candidate{Name="Candidate",NormalizedName="CANDIDATE"};
        db.AddRange(p,c);await db.SaveChangesAsync();
        db.Assessments.AddRange(new Assessment{PositionId=p.Id},new Assessment{PositionId=p.Id},new Assessment{PositionId=p.Id,CandidateId=c.Id});await db.SaveChangesAsync();
        db.Assessments.Add(new Assessment{PositionId=p.Id,CandidateId=c.Id});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]public async Task OptimisticConcurrencyRejectsStaleUpdates()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();var options=new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options;
        await using var first=new AtsDbContext(options);await first.Database.EnsureCreatedAsync();var c=new Candidate{Name="Candidate"};first.Candidates.Add(c);await first.SaveChangesAsync();
        await using var second=new AtsDbContext(options);var old=await second.Candidates.SingleAsync();c.Notes="First change";await first.SaveChangesAsync();old.Notes="Stale change";await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>second.SaveChangesAsync());
    }
}
