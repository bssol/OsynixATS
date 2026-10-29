using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261020180000_CandidateLinkedInProfile")]
public sealed class CandidateLinkedInProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "LinkedInUrl",
        table: "Candidates",
        type: "TEXT",
        nullable: false,
        defaultValue: "");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "LinkedInUrl",
        table: "Candidates");
}
