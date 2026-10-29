using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupportSessionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportConversations_EmployerAccessRequestId",
                table: "SupportConversations");

            migrationBuilder.DropIndex(
                name: "IX_SupportConversations_UserId",
                table: "SupportConversations");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedUtc",
                table: "SupportConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssueType",
                table: "SupportConversations",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                defaultValue: "General Support");

            migrationBuilder.Sql("UPDATE SupportConversations SET IssueType = 'Subscription & Payment' WHERE EmployerAccessRequestId IS NOT NULL;");
            migrationBuilder.Sql("UPDATE SupportConversations SET ClosedUtc = UpdatedUtc WHERE Status = 'Closed';");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_EmployerAccessRequestId",
                table: "SupportConversations",
                column: "EmployerAccessRequestId",
                unique: true,
                filter: "EmployerAccessRequestId IS NOT NULL AND Status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_UserId",
                table: "SupportConversations",
                column: "UserId",
                unique: true,
                filter: "EmployerAccessRequestId IS NULL AND Subject = 'Osynix Support' AND Status = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportConversations_EmployerAccessRequestId",
                table: "SupportConversations");

            migrationBuilder.DropIndex(
                name: "IX_SupportConversations_UserId",
                table: "SupportConversations");

            migrationBuilder.DropColumn(
                name: "ClosedUtc",
                table: "SupportConversations");

            migrationBuilder.DropColumn(
                name: "IssueType",
                table: "SupportConversations");

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
        }
    }
}
