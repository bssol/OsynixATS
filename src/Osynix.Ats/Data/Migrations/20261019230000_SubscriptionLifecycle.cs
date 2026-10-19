using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261019230000_SubscriptionLifecycle")]
public class SubscriptionLifecycleMigration:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name:"SubscriptionAllowanceId",table:"EmployerWorkspaces",type:"TEXT",nullable:false,defaultValue:Guid.Empty);
        migrationBuilder.AddColumn<Guid>(name:"AllowanceId",table:"SubscriptionUsage",type:"TEXT",nullable:false,defaultValue:Guid.Empty);
        migrationBuilder.CreateTable(name:"SubscriptionChanges",columns:table=>new{
            Id=table.Column<Guid>(type:"TEXT",nullable:false),
            WorkspaceId=table.Column<Guid>(type:"TEXT",nullable:false),
            PlanId=table.Column<Guid>(type:"TEXT",nullable:false),
            PlanName=table.Column<string>(type:"TEXT",nullable:false),
            PlanSnapshotJson=table.Column<string>(type:"TEXT",nullable:false),
            Kind=table.Column<string>(type:"TEXT",nullable:false),
            Status=table.Column<string>(type:"TEXT",nullable:false),
            EffectiveDate=table.Column<DateOnly>(type:"TEXT",nullable:false),
            BillingPeriod=table.Column<string>(type:"TEXT",nullable:false),
            Currency=table.Column<string>(type:"TEXT",nullable:false),
            Price=table.Column<decimal>(type:"TEXT",nullable:true),
            AppliedUtc=table.Column<DateTime>(type:"TEXT",nullable:true),
            CreatedUtc=table.Column<DateTime>(type:"TEXT",nullable:false),
            UpdatedUtc=table.Column<DateTime>(type:"TEXT",nullable:false),
            Revision=table.Column<Guid>(type:"TEXT",nullable:false)
        },constraints:table=>{
            table.PrimaryKey("PK_SubscriptionChanges",x=>x.Id);
            table.ForeignKey("FK_SubscriptionChanges_EmployerWorkspaces_WorkspaceId",x=>x.WorkspaceId,"EmployerWorkspaces","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_SubscriptionChanges_SubscriptionPlans_PlanId",x=>x.PlanId,"SubscriptionPlans","Id",onDelete:ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name:"IX_SubscriptionChanges_WorkspaceId",table:"SubscriptionChanges",column:"WorkspaceId",unique:true,filter:"Status = 'Scheduled'");
        migrationBuilder.CreateIndex(name:"IX_SubscriptionChanges_PlanId",table:"SubscriptionChanges",column:"PlanId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SubscriptionChanges");
        migrationBuilder.DropColumn("SubscriptionAllowanceId","EmployerWorkspaces");
        migrationBuilder.DropColumn("AllowanceId","SubscriptionUsage");
    }
}
