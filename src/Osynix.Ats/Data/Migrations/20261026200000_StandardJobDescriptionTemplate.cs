using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261026200000_StandardJobDescriptionTemplate")]
public partial class StandardJobDescriptionTemplate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Industry", table: "Positions", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "Seniority", table: "Positions", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "JobDescriptionHtml", table: "Positions", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "JdNeedsAttentionJson", table: "Positions", type: "TEXT", nullable: false, defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN JdNeedsAttentionJson");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN JobDescriptionHtml");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN Seniority");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN Industry");
    }
}
