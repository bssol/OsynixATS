using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPlansAndUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionActivatedUtc",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionBilling",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionCurrency",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionName",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SubscriptionPrice",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionTermsJson",
                table: "EmployerWorkspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillingPeriod",
                table: "EmployerAccessRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "EmployerAccessRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PaymentConfirmed",
                table: "EmployerAccessRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PlanSnapshotJson",
                table: "EmployerAccessRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedPrice",
                table: "EmployerAccessRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId",
                table: "EmployerAccessRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountCategory = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Published = table.Column<bool>(type: "INTEGER", nullable: false),
                    PricingConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonthlyPkr = table.Column<decimal>(type: "TEXT", nullable: false),
                    AnnualPkr = table.Column<decimal>(type: "TEXT", nullable: false),
                    MonthlyUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    AnnualUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    TermsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    MonthUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PositionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionUsage_EmployerWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "EmployerWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployerWorkspaces_SubscriptionPlanId",
                table: "EmployerWorkspaces",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerAccessRequests_SubscriptionPlanId",
                table: "EmployerAccessRequests",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_AccountCategory_Code",
                table: "SubscriptionPlans",
                columns: new[] { "AccountCategory", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUsage_ApplicationId_Kind",
                table: "SubscriptionUsage",
                columns: new[] { "ApplicationId", "Kind" },
                unique: true,
                filter: "ApplicationId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUsage_WorkspaceId_Kind_MonthUtc",
                table: "SubscriptionUsage",
                columns: new[] { "WorkspaceId", "Kind", "MonthUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_EmployerAccessRequests_SubscriptionPlans_SubscriptionPlanId",
                table: "EmployerAccessRequests",
                column: "SubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployerWorkspaces_SubscriptionPlans_SubscriptionPlanId",
                table: "EmployerWorkspaces",
                column: "SubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployerAccessRequests_SubscriptionPlans_SubscriptionPlanId",
                table: "EmployerAccessRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployerWorkspaces_SubscriptionPlans_SubscriptionPlanId",
                table: "EmployerWorkspaces");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "SubscriptionUsage");

            migrationBuilder.DropIndex(
                name: "IX_EmployerWorkspaces_SubscriptionPlanId",
                table: "EmployerWorkspaces");

            migrationBuilder.DropIndex(
                name: "IX_EmployerAccessRequests_SubscriptionPlanId",
                table: "EmployerAccessRequests");

            migrationBuilder.DropColumn(
                name: "SubscriptionActivatedUtc",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "SubscriptionBilling",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "SubscriptionCurrency",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "SubscriptionName",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "SubscriptionPrice",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "SubscriptionTermsJson",
                table: "EmployerWorkspaces");

            migrationBuilder.DropColumn(
                name: "BillingPeriod",
                table: "EmployerAccessRequests");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "EmployerAccessRequests");

            migrationBuilder.DropColumn(
                name: "PaymentConfirmed",
                table: "EmployerAccessRequests");

            migrationBuilder.DropColumn(
                name: "PlanSnapshotJson",
                table: "EmployerAccessRequests");

            migrationBuilder.DropColumn(
                name: "QuotedPrice",
                table: "EmployerAccessRequests");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "EmployerAccessRequests");
        }
    }
}
