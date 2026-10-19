using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261019190000_SubscriptionRenewalsAndPromotions")]
public sealed class SubscriptionRenewalsAndPromotions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "PromotionEndsOn", table: "SubscriptionPlans", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "PromotionLabel", table: "SubscriptionPlans", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<decimal>(name: "PromotionPercent", table: "SubscriptionPlans", type: "TEXT", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<DateOnly>(name: "PromotionStartsOn", table: "SubscriptionPlans", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "SubscriptionReminderEmailForDate", table: "EmployerWorkspaces", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "SubscriptionReminderNoticeForDate", table: "EmployerWorkspaces", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "SubscriptionRenewalDate", table: "EmployerWorkspaces", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PromotionEndsOn", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "PromotionLabel", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "PromotionPercent", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "PromotionStartsOn", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "SubscriptionReminderEmailForDate", table: "EmployerWorkspaces");
        migrationBuilder.DropColumn(name: "SubscriptionReminderNoticeForDate", table: "EmployerWorkspaces");
        migrationBuilder.DropColumn(name: "SubscriptionRenewalDate", table: "EmployerWorkspaces");
    }
}
