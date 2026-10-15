using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployerWorkspaceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployerWorkspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    OperatingType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AboutUs = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BusinessEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ContactNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LogoContent = table.Column<byte[]>(type: "BLOB", nullable: true),
                    LogoContentType = table.Column<string>(type: "TEXT", nullable: false),
                    ShowBusinessContacts = table.Column<bool>(type: "INTEGER", nullable: false),
                    CareersPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    FreeRecruiterSeats = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalRecruiterSeats = table.Column<int>(type: "INTEGER", nullable: false),
                    FreePositionSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalPositionSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamTalentPoolEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerWorkspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployerWorkspaces_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployerAccessRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AdminResponse = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RespondedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerAccessRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployerAccessRequests_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployerAccessRequests_WorkspaceId",
                table: "EmployerAccessRequests",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerWorkspaces_OwnerId",
                table: "EmployerWorkspaces",
                column: "OwnerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployerAccessRequests");

            migrationBuilder.DropTable(
                name: "EmployerWorkspaces");
        }
    }
}
