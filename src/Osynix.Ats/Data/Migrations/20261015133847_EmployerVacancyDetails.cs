using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployerVacancyDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HiringManagerEmail",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HiringManagerPhone",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SalaryCurrency",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "PKR");

            migrationBuilder.AddColumn<decimal>(
                name: "SalaryMaximum",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalaryMinimum",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalaryPeriod",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<bool>(
                name: "ShowSalaryPublicly",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WorkArrangement",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "On-Site");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "HiringManagerEmail",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "HiringManagerPhone",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "SalaryCurrency",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "SalaryMaximum",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "SalaryMinimum",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "SalaryPeriod",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ShowSalaryPublicly",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "WorkArrangement",
                table: "Positions");
        }
    }
}
