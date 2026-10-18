using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketplaceConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FreelancerId = table.Column<string>(type: "TEXT", nullable: false),
                    Agency = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmployerReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FreelancerReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastMessageUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceConversations_AspNetUsers_FreelancerId",
                        column: x => x.FreelancerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceConversations_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PositionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Start = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    EmailDelivery = table.Column<string>(type: "TEXT", nullable: false),
                    RespondedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_MarketplaceConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "MarketplaceConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderId = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    InvitationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AttachmentName = table.Column<string>(type: "TEXT", nullable: false),
                    AttachmentType = table.Column<string>(type: "TEXT", nullable: false),
                    AttachmentContent = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceMessages_MarketplaceConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "MarketplaceConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceMessages_ProjectInvitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "ProjectInvitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceConversations_FreelancerId",
                table: "MarketplaceConversations",
                column: "FreelancerId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceConversations_WorkspaceId_FreelancerId_Agency",
                table: "MarketplaceConversations",
                columns: new[] { "WorkspaceId", "FreelancerId", "Agency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceMessages_ConversationId_CreatedUtc",
                table: "MarketplaceMessages",
                columns: new[] { "ConversationId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceMessages_InvitationId",
                table: "MarketplaceMessages",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_ConversationId_Status",
                table: "ProjectInvitations",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_PositionId",
                table: "ProjectInvitations",
                column: "PositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceMessages");

            migrationBuilder.DropTable(
                name: "ProjectInvitations");

            migrationBuilder.DropTable(
                name: "MarketplaceConversations");
        }
    }
}
