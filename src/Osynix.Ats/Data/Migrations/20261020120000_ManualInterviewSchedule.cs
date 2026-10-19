using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManualInterviewSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InterviewDateTimeUtc",
                table: "Assessments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterviewTimeZone",
                table: "Assessments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterviewDateTimeUtc",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "InterviewTimeZone",
                table: "Assessments");
        }
    }
}
