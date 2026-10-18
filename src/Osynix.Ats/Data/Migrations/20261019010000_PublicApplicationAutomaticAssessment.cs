using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261019010000_PublicApplicationAutomaticAssessment")]
public class PublicApplicationAutomaticAssessment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AutoAssessmentStatus", table: "Assessments", type: "TEXT", nullable: false, defaultValue: "Not Requested");
        migrationBuilder.AddColumn<string>(name: "AutoAssessmentError", table: "Assessments", type: "TEXT", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AutoAssessmentStatus", table: "Assessments");
        migrationBuilder.DropColumn(name: "AutoAssessmentError", table: "Assessments");
    }
}
