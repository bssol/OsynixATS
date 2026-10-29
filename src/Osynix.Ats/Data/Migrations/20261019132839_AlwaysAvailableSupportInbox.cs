using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlwaysAvailableSupportInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmployerAccessRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    LastMessageUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AdminReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportConversations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportConversations_EmployerAccessRequests_EmployerAccessRequestId",
                        column: x => x.EmployerAccessRequestId,
                        principalTable: "EmployerAccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportConversations_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    AttachmentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AttachmentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AttachmentContent = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportMessages_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportMessages_SupportConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "SupportConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_EmployerAccessRequestId",
                table: "SupportConversations",
                column: "EmployerAccessRequestId",
                unique: true,
                filter: "EmployerAccessRequestId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_UserId",
                table: "SupportConversations",
                column: "UserId",
                unique: true,
                filter: "EmployerAccessRequestId IS NULL AND Subject = 'Osynix Support'");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_WorkspaceId",
                table: "SupportConversations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_ConversationId_CreatedUtc",
                table: "SupportMessages",
                columns: new[] { "ConversationId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_SenderId",
                table: "SupportMessages",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportMessages");

            migrationBuilder.DropTable(
                name: "SupportConversations");
        }
    }
}
