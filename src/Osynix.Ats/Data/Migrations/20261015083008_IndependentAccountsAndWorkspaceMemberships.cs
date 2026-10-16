using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class IndependentAccountsAndWorkspaceMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "Employer");

            migrationBuilder.AddColumn<bool>(
                name: "MasterTalentPoolEnabled",
                table: "EmployerWorkspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "Candidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Candidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.CreateTable(
                name: "TalentPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false),
                    EmailKey = table.Column<string>(type: "TEXT", nullable: false),
                    PhoneKey = table.Column<string>(type: "TEXT", nullable: false),
                    CanonicalCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NeedsReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentPeople", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CanCreateClients = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanCreatePositions = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanExportReports = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanViewTalentPool = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanUseMasterPool = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_PersonId",
                table: "Candidates",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_WorkspaceId",
                table: "Candidates",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentPeople_EmailKey",
                table: "TalentPeople",
                column: "EmailKey");

            migrationBuilder.CreateIndex(
                name: "IX_TalentPeople_PhoneKey",
                table: "TalentPeople",
                column: "PhoneKey");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemberships_UserId",
                table: "WorkspaceMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemberships_WorkspaceId_UserId",
                table: "WorkspaceMemberships",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_EmployerWorkspaces_WorkspaceId",
                table: "Candidates",
                column: "WorkspaceId",
                principalTable: "EmployerWorkspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_TalentPeople_PersonId",
                table: "Candidates",
                column: "PersonId",
                principalTable: "TalentPeople",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("UPDATE EmployerWorkspaces SET MasterTalentPoolEnabled = TeamTalentPoolEnabled;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_EmployerWorkspaces_WorkspaceId",
                table: "Candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_TalentPeople_PersonId",
                table: "Candidates");

            migrationBuilder.DropTable(
                name: "TalentPeople");

            migrationBuilder.DropTable(
                name: "WorkspaceMemberships");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_PersonId",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_WorkspaceId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "MasterTalentPoolEnabled",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "AspNetUsers");
        }
    }
}
