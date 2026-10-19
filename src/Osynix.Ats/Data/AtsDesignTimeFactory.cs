using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Osynix.Ats.Data;

// Scaffolding migrations must not run the live application's startup/backfill.
public sealed class AtsDesignTimeFactory : IDesignTimeDbContextFactory<AtsDbContext>
{
    public AtsDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite("Data Source=:memory:").Options);
}
