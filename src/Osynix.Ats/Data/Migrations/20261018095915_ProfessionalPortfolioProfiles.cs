using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalPortfolioProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaseStudies",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Education",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Experience",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HiringMarkets",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Industries",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Languages",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SeniorityLevels",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WeeklyAvailability",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkingStyle",
                table: "FreelancerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CaseStudies",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "Education",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "HiringMarkets",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "Industries",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "Languages",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "SeniorityLevels",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "WeeklyAvailability",
                table: "FreelancerProfiles");

            migrationBuilder.DropColumn(
                name: "WorkingStyle",
                table: "FreelancerProfiles");
        }
    }
}
