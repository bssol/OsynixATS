using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261025200000_EmployerSubscriptionCatalogue")]
public class EmployerSubscriptionCatalogue:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var codes=new[]{"free","starter","growth","pro"};
        var slots=new[]{3,5,10,20};var seats=new[]{0,1,3,5};
        var assessments=new[]{30,150,500,1500};var jds=new[]{3,10,30,100};
        var pkr=new[]{0,7500,15000,30000};var usd=new[]{0,25,50,99};
        var descriptions=new[]{"Explore the Employer workspace","For small teams hiring for several roles","For growing teams with steady hiring needs","For high-volume hiring across larger teams"};
        for(var i=0;i<codes.Length;i++){
            var terms=JsonSerializer.Serialize(new{PositionSlots=slots[i],InternalSeats=seats[i],MonthlyAssessments=assessments[i],MonthlyJdAnalyses=jds[i],ApplicationsPerPosition=(int?)null,Marketplace=false,MasterPool=i>=2,MeetingIntegrations=i>=2,Reports=i>0,EmailTemplates=i>=2,HiringManagerReviews=i>0,FreelancerDirectory=i>0,MonthlyFreelancerApproaches=i==1?(int?)5:null,ZoomIntegration=i==3});
            migrationBuilder.Sql($"UPDATE SubscriptionPlans SET Published=1,PricingConfirmed=1,MonthlyPkr='{pkr[i]}',AnnualPkr='{pkr[i]*10}',MonthlyUsd='{usd[i]}',AnnualUsd='{usd[i]*10}',TermsJson='{terms}',Description='{descriptions[i]}',Revision='{Guid.NewGuid().ToString().ToUpperInvariant()}' WHERE AccountCategory='Employer' AND Code='{codes[i]}';");
        }
        // Bring existing Free Employer workspaces onto the approved baseline. Paid agreements and explicit add-ons are retained.
        migrationBuilder.Sql("UPDATE EmployerWorkspaces SET SubscriptionTermsJson=(SELECT TermsJson FROM SubscriptionPlans WHERE Id=EmployerWorkspaces.SubscriptionPlanId),FreePositionSlots=3,FreeRecruiterSeats=0 WHERE Kind='Employer' AND AgencyEnabled=0 AND SubscriptionPlanId IN(SELECT Id FROM SubscriptionPlans WHERE AccountCategory='Employer' AND Code='free');");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalogue edits are business records and are retained on schema rollback.
    }
}
