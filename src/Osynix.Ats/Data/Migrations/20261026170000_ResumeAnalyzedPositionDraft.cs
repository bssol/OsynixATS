using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261026170000_ResumeAnalyzedPositionDraft")]
public partial class ResumeAnalyzedPositionDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "DraftAnalysisSignature", table: "Positions", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "DraftWorkflowStep", table: "Positions", type: "TEXT", nullable: false, defaultValue: "basics");
        migrationBuilder.AddColumn<bool>(name: "DraftSourceReviewed", table: "Positions", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "DraftCriteriaReviewed", table: "Positions", type: "INTEGER", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN DraftCriteriaReviewed");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN DraftSourceReviewed");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN DraftWorkflowStep");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN DraftAnalysisSignature");
    }
}
