using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class InterviewCoordination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterviewBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizerId = table.Column<string>(type: "TEXT", nullable: false),
                    CandidateEmail = table.Column<string>(type: "TEXT", nullable: false),
                    ManagerEmail = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    MeetingLink = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedToken = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProposedStartUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProposedTimeZone = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseNotes = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseSource = table.Column<string>(type: "TEXT", nullable: false),
                    RespondedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewBookings_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterviewBookings_HiringSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "HiringSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InterviewDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InterviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewDeliveries_InterviewBookings_InterviewId",
                        column: x => x.InterviewId,
                        principalTable: "InterviewBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewBookings_AssessmentId",
                table: "InterviewBookings",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewBookings_SubmissionId",
                table: "InterviewBookings",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewBookings_TokenHash",
                table: "InterviewBookings",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterviewDeliveries_InterviewId",
                table: "InterviewDeliveries",
                column: "InterviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterviewDeliveries");

            migrationBuilder.DropTable(
                name: "InterviewBookings");
        }
    }
}
