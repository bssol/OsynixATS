using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecruiterClientWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecruiterResponse",
                table: "HiringSubmissions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RecruiterResponseUtc",
                table: "HiringSubmissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecruiterActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipientId = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Feedback = table.Column<string>(type: "TEXT", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FeedbackUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruiterActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruiterActions_AspNetUsers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecruiterActions_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecruiterActions_HiringSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "HiringSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruiterActions_AssessmentId",
                table: "RecruiterActions",
                column: "AssessmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecruiterActions_RecipientId",
                table: "RecruiterActions",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruiterActions_SubmissionId",
                table: "RecruiterActions",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruiterActions");

            migrationBuilder.DropColumn(
                name: "RecruiterResponse",
                table: "HiringSubmissions");

            migrationBuilder.DropColumn(
                name: "RecruiterResponseUtc",
                table: "HiringSubmissions");
        }
    }
}
