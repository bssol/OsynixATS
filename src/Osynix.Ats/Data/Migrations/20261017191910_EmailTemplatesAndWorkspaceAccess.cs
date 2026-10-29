using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplatesAndWorkspaceAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailTemplatesEnabled",
                table: "EmployerWorkspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BodyHtml = table.Column<string>(type: "TEXT", maxLength: 30000, nullable: false),
                    Custom = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailTemplates_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Key",
                table: "EmailTemplates",
                column: "Key",
                unique: true,
                filter: "WorkspaceId IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_WorkspaceId_Key",
                table: "EmailTemplates",
                columns: new[] { "WorkspaceId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailTemplates");

            // SQLite supports this directly; avoid rebuilding the workspace table and its many foreign keys.
            migrationBuilder.Sql("ALTER TABLE \"EmployerWorkspaces\" DROP COLUMN \"EmailTemplatesEnabled\";");
        }
    }
}
