using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261026010000_NormalizeEmployerPlanRevisions")]
public class NormalizeEmployerPlanRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Microsoft.Data.Sqlite binds Guid values in uppercase. A lowercase raw-SQL
        // revision reads as the same Guid but fails the case-sensitive UPDATE predicate.
        migrationBuilder.Sql("UPDATE SubscriptionPlans SET Revision=upper(Revision) WHERE AccountCategory='Employer' AND Revision<>upper(Revision);");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
