using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class CareersPageBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CareersColorScheme",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "Osynix");

            migrationBuilder.AddColumn<byte[]>(
                name: "CareersCoverContent",
                table: "EmployerWorkspaces",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CareersCoverContentType",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CareersCoverMode",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "Stock");

            migrationBuilder.AddColumn<int>(
                name: "CareersStockCover",
                table: "EmployerWorkspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CareersColorScheme",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "CareersCoverContent",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "CareersCoverContentType",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "CareersCoverMode",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "CareersStockCover",
                table: "EmployerWorkspaces");
        }
    }
}
