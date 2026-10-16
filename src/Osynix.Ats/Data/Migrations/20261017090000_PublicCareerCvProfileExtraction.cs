using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261017090000_PublicCareerCvProfileExtraction")]
public partial class PublicCareerCvProfileExtraction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "CvProfileStatus", table: "Candidates", type: "TEXT", nullable: false, defaultValue: "Not Requested");
        migrationBuilder.AddColumn<string>(name: "CvProfileError", table: "Candidates", type: "TEXT", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CvProfileStatus", table: "Candidates");
        migrationBuilder.DropColumn(name: "CvProfileError", table: "Candidates");
    }
}
