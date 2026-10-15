using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployerTeamMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_NormalizedName",
                table: "Clients");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Clients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployerWorkspaceId",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployerInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedToken = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Delivery = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployerInvitations_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_WorkspaceId",
                table: "Positions",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_NormalizedName",
                table: "Clients",
                column: "NormalizedName",
                unique: true,
                filter: "\"WorkspaceId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_WorkspaceId_NormalizedName",
                table: "Clients",
                columns: new[] { "WorkspaceId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_EmployerWorkspaceId",
                table: "AspNetUsers",
                column: "EmployerWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerInvitations_TokenHash",
                table: "EmployerInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployerInvitations_WorkspaceId_NormalizedEmail",
                table: "EmployerInvitations",
                columns: new[] { "WorkspaceId", "NormalizedEmail" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_EmployerWorkspaces_EmployerWorkspaceId",
                table: "AspNetUsers",
                column: "EmployerWorkspaceId",
                principalTable: "EmployerWorkspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_EmployerWorkspaces_WorkspaceId",
                table: "Clients",
                column: "WorkspaceId",
                principalTable: "EmployerWorkspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Positions_EmployerWorkspaces_WorkspaceId",
                table: "Positions",
                column: "WorkspaceId",
                principalTable: "EmployerWorkspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_EmployerWorkspaces_EmployerWorkspaceId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Clients_EmployerWorkspaces_WorkspaceId",
                table: "Clients");

            migrationBuilder.DropForeignKey(
                name: "FK_Positions_EmployerWorkspaces_WorkspaceId",
                table: "Positions");

            migrationBuilder.DropTable(
                name: "EmployerInvitations");

            migrationBuilder.DropIndex(
                name: "IX_Positions_WorkspaceId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Clients_NormalizedName",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_WorkspaceId_NormalizedName",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_EmployerWorkspaceId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "EmployerWorkspaceId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_NormalizedName",
                table: "Clients",
                column: "NormalizedName",
                unique: true);
        }
    }
}
