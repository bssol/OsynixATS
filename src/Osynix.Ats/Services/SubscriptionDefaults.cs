using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public static class SubscriptionDefaults
{
    public static async Task Initialize(AtsDbContext db,CancellationToken ct=default)
    {
        if(!await db.SubscriptionPlans.AnyAsync(ct)){
            foreach(var category in new[]{"Freelancer","Employer","Agency"})foreach(var code in new[]{"free","starter","growth","pro"}){
                var free=code=="free";var freelancer=category=="Freelancer";var growth=code is "growth" or "pro";
                var terms=new SubscriptionTerms{PositionSlots=freelancer?code switch{"free"=>1,"starter"=>3,"growth"=>10,_=>25}:3,InternalSeats=freelancer?0:1,
                    MonthlyAssessments=freelancer?code switch{"free"=>20,"starter"=>50,"growth"=>200,_=>500}:null,MonthlyJdAnalyses=freelancer?code switch{"free"=>2,"starter"=>5,"growth"=>15,_=>30}:null,
                    ApplicationsPerPosition=freelancer?code switch{"growth"=>15,"pro"=>null,_=>10}:null,Marketplace=freelancer&&!free,MasterPool=freelancer&&growth,MeetingIntegrations=freelancer&&growth,Reports=!freelancer||!free};
                if(category=="Employer")terms=EmployerTerms(code);
                var monthly=category=="Employer"?code switch{"starter"=>7500m,"growth"=>15000m,"pro"=>30000m,_=>0m}:0m;
                var usd=category=="Employer"?code switch{"starter"=>25m,"growth"=>50m,"pro"=>99m,_=>0m}:0m;
                db.SubscriptionPlans.Add(new(){AccountCategory=category,Code=code,Name=char.ToUpperInvariant(code[0])+code[1..],Published=free||freelancer||category=="Employer",PricingConfirmed=free||category=="Employer",MonthlyPkr=monthly,AnnualPkr=monthly*10,MonthlyUsd=usd,AnnualUsd=usd*10,TermsJson=JsonSerializer.Serialize(terms),Description=category=="Employer"?EmployerDescription(code):free?"Core workspace access":freelancer?"Independent recruiting workspace":"Draft — configure benefits before publishing"});
            }
            await db.SaveChangesAsync(ct);
        }
        foreach(var w in await db.EmployerWorkspaces.Where(w=>w.SubscriptionPlanId==null).ToListAsync(ct)){
            await AssignFree(db,w,ct);
            if(w.Kind==AccountTypes.Freelance&&SubscriptionRules.Terms(w)?.Marketplace==false){var profile=await db.FreelancerProfiles.SingleOrDefaultAsync(p=>p.UserId==w.OwnerId,ct);if(profile is not null){profile.Published=false;profile.Availability="Not Available";}}
        }
        await db.SaveChangesAsync(ct);
    }
    public static async Task AssignFree(AtsDbContext db,EmployerWorkspace w,CancellationToken ct=default)
    {
        var category=SubscriptionRules.Category(w);
        var p=await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p=>p.AccountCategory==category&&p.Code=="free",ct);
        if(p is null)return;
        // Keep prior explicit Admin capacity grants when bringing existing accounts onto Free.
        var seats=w.FreeRecruiterSeats;var slots=w.FreePositionSlots;
        SubscriptionRules.Apply(w,p,price:0);
        w.AdditionalRecruiterSeats+=Math.Max(0,seats-Math.Max(w.FreeRecruiterSeats,category=="Employer"?1:0));w.AdditionalPositionSlots+=Math.Max(0,slots-w.FreePositionSlots);
    }
    public static SubscriptionTerms EmployerTerms(string code)=>new(){
        PositionSlots=code switch{"free"=>3,"starter"=>5,"growth"=>10,_=>20},
        InternalSeats=code switch{"free"=>0,"starter"=>1,"growth"=>3,_=>5},
        MonthlyAssessments=code switch{"free"=>30,"starter"=>150,"growth"=>500,_=>1500},
        MonthlyJdAnalyses=code switch{"free"=>3,"starter"=>10,"growth"=>30,_=>100},
        HiringManagerReviews=code!="free",FreelancerDirectory=code!="free",
        MonthlyFreelancerApproaches=code=="starter"?5:null,
        MasterPool=code is "growth" or "pro",MeetingIntegrations=code is "growth" or "pro",
        ZoomIntegration=code=="pro",EmailTemplates=code is "growth" or "pro",Reports=code!="free"
    };
    public static string EmployerDescription(string code)=>code switch{"free"=>"Explore the Employer workspace","starter"=>"For small teams hiring for several roles","growth"=>"For growing teams with steady hiring needs",_=>"For high-volume hiring across larger teams"};
}
