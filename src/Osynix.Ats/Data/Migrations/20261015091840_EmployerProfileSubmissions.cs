using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployerProfileSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployerProfileSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssessmentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: false),
                    FeedbackUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerProfileSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployerProfileSubmissions_AspNetUsers_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployerProfileSubmissions_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployerProfileSubmissions_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployerProfileSubmissions_AssessmentId",
                table: "EmployerProfileSubmissions",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerProfileSubmissions_SubmittedBy",
                table: "EmployerProfileSubmissions",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerProfileSubmissions_WorkspaceId_AssessmentId",
                table: "EmployerProfileSubmissions",
                columns: new[] { "WorkspaceId", "AssessmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployerProfileSubmissions");
        }
    }
}
