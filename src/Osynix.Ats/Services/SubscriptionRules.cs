using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public static class SubscriptionRules
{
    public static int Tier(string code)=>code switch{"free"=>0,"starter"=>1,"growth"=>2,"pro"=>3,_=>throw new InvalidOperationException("Unknown subscription tier.")};
    public static string Category(EmployerWorkspace w)=>w.Kind==AccountTypes.Freelance?"Freelancer":w.AgencyEnabled?"Agency":"Employer";
    public static SubscriptionTerms Terms(SubscriptionPlan p)=>JsonSerializer.Deserialize<SubscriptionTerms>(p.TermsJson)??new();
    public static SubscriptionTerms? Terms(EmployerWorkspace w)=>string.IsNullOrEmpty(w.SubscriptionTermsJson)?null:JsonSerializer.Deserialize<SubscriptionTerms>(w.SubscriptionTermsJson);
    public static decimal? ListPrice(SubscriptionPlan p,string billing,string currency)=>!p.PricingConfirmed?null:(billing,currency) switch{("Monthly","PKR")=>p.MonthlyPkr,("Annual","PKR")=>p.AnnualPkr,("Monthly","USD")=>p.MonthlyUsd,_=>p.AnnualUsd};
    public static bool PromotionActive(SubscriptionPlan p,DateOnly today)=>p.PromotionPercent>0&&p.PromotionPercent<100&&(p.PromotionStartsOn is null||today>=p.PromotionStartsOn)&&(p.PromotionEndsOn is null||today<=p.PromotionEndsOn);
    public static decimal? Price(SubscriptionPlan p,string billing,string currency){var price=ListPrice(p,billing,currency);return price is decimal amount&&PromotionActive(p,PakistanToday())?Math.Round(amount*(100m-p.PromotionPercent)/100m,2,MidpointRounding.AwayFromZero):price;}
    public static DateOnly PakistanToday(){try{return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi")));}catch(TimeZoneNotFoundException){return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time")));}}
    public static DateTime Month(DateTime utc)=>new(utc.Year,utc.Month,1,0,0,0,DateTimeKind.Utc);
    public static async Task<bool> MarketplaceAllowed(AtsDbContext db,string userId)
    {
        var w=await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.OwnerId==userId);
        if(w is not null&&await SubscriptionLifecycle.ApplyDue(db,w.Id))w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(x=>x.Id==w.Id);
        return w is not null&&w.Kind==AccountTypes.Freelance&&Terms(w)?.Marketplace==true&&await db.Users.AnyAsync(u=>u.Id==userId&&u.AccountType==AccountTypes.Freelance&&u.Status=="Active"&&u.EmailConfirmed);
    }
    public static async Task RequireMarketplace(AtsDbContext db,string userId)
    {
        if(!await MarketplaceAllowed(db,userId))throw new UnauthorizedAccessException("Marketplace profiles, availability and invitations require Starter or a higher Freelancer plan. Upgrade in Settings → Subscriptions.");
    }
    public static async Task RequireFreelancerDirectory(AtsDbContext db,Guid workspaceId)
    {
        await SubscriptionLifecycle.ApplyDue(db,workspaceId);
        var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==workspaceId);
        if(workspace.Kind==AccountTypes.Freelance||Terms(workspace)?.FreelancerDirectory==false)
            throw new UnauthorizedAccessException("Freelancer profiles and new outreach require Starter or a higher Employer plan. Upgrade in Settings → Subscriptions.");
    }
    public static async Task RequireHiringReviews(AtsDbContext db,Guid? workspaceId)
    {
        if(workspaceId is not Guid id)return;
        await SubscriptionLifecycle.ApplyDue(db,id);
        var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==id);
        if(Terms(workspace)?.HiringManagerReviews==false)
            throw new UnauthorizedAccessException("Hiring Manager review links require a plan that includes this feature. Upgrade in Settings → Subscriptions.");
    }
    public static Task<int> FreelancerApproaches(AtsDbContext db,Guid workspaceId){var month=Month(DateTime.UtcNow);return db.SubscriptionUsage.Where(u=>u.WorkspaceId==workspaceId&&u.MonthUtc==month&&u.Kind.StartsWith("FreelancerApproach:")).Select(u=>u.Kind).Distinct().CountAsync();}
    public static async Task ChargeFreelancerApproach(AtsDbContext db,Guid workspaceId,string freelancerId)
    {
        var owns=db.Database.CurrentTransaction is null;
        await using var tx=owns?await db.Database.BeginTransactionAsync():null;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmployerWorkspaces SET Id=Id WHERE Id={workspaceId}");
        await RequireFreelancerDirectory(db,workspaceId);
        var w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==workspaceId);
        var month=Month(DateTime.UtcNow);var kind="FreelancerApproach:"+freelancerId;
        if(!await db.SubscriptionUsage.AnyAsync(u=>u.WorkspaceId==workspaceId&&u.Kind==kind&&u.MonthUtc==month)){
            if(Terms(w)?.MonthlyFreelancerApproaches is int limit&&await FreelancerApproaches(db,workspaceId)>=limit)
                throw new InvalidOperationException($"Your plan allows approaching {limit} unique freelancers per month. Continue existing conversations or upgrade your plan.");
            db.SubscriptionUsage.Add(new(){WorkspaceId=workspaceId,AllowanceId=w.SubscriptionAllowanceId,Kind=kind,MonthUtc=month});
            await db.SaveChangesAsync();
        }
        if(tx is not null)await tx.CommitAsync();
    }
    // A database write lock serializes allowance checks across browsers and workers.
    // Call inside the intake transaction to reserve an application and its AI allowance together.
    public static async Task Charge(AtsDbContext db,Guid workspaceId,string kind,Guid? applicationId=null,CancellationToken ct=default)
    {
        var owns=db.Database.CurrentTransaction is null;
        await using var tx=owns?await db.Database.BeginTransactionAsync(ct):null;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmployerWorkspaces SET Id=Id WHERE Id={workspaceId}",ct);
        await SubscriptionLifecycle.ApplyDue(db,workspaceId,ct:ct);
        if(applicationId is Guid id&&await db.SubscriptionUsage.AnyAsync(u=>u.ApplicationId==id&&u.Kind==kind,ct)){if(tx is not null)await tx.CommitAsync(ct);return;}
        var w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==workspaceId,ct);
        var terms=Terms(w);var limit=kind=="JD"?terms?.MonthlyJdAnalyses:terms?.MonthlyAssessments;var month=Month(DateTime.UtcNow);
        if(limit is int cap&&await db.SubscriptionUsage.CountAsync(u=>u.WorkspaceId==workspaceId&&u.AllowanceId==w.SubscriptionAllowanceId&&u.Kind==kind&&u.MonthUtc==month,ct)>=cap)
            throw new InvalidOperationException($"Your workspace's monthly {(kind=="JD"?"JD analysis":"AI assessment")} allowance is exhausted. Request a plan upgrade in Settings → Subscriptions.");
        db.SubscriptionUsage.Add(new(){WorkspaceId=workspaceId,AllowanceId=w.SubscriptionAllowanceId,Kind=kind,MonthUtc=month,ApplicationId=applicationId});
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);
    }
    public static async Task<bool> Accepting(AtsDbContext db,EmployerWorkspace w,Guid positionId)
    {
        await SubscriptionLifecycle.ApplyDue(db,w.Id);w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(x=>x.Id==w.Id);
        var terms=Terms(w);if(terms is null)return true;
        if(terms.ApplicationsPerPosition is int cap&&await db.SubscriptionUsage.CountAsync(a=>a.Kind=="Application"&&a.PositionId==positionId)>=cap)return false;
        var month=Month(DateTime.UtcNow);
        return terms.MonthlyAssessments is not int limit||await db.SubscriptionUsage.CountAsync(u=>u.WorkspaceId==w.Id&&u.AllowanceId==w.SubscriptionAllowanceId&&u.Kind=="Assessment"&&u.MonthUtc==month)<limit;
    }
    public static void Apply(EmployerWorkspace w,SubscriptionPlan p,string billing="Monthly",string currency="PKR",decimal? price=null,DateOnly? effectiveDate=null)
    {
        var t=Terms(p);w.SubscriptionPlanId=p.Id;w.SubscriptionName=p.Name;w.SubscriptionTermsJson=p.TermsJson;
        w.SubscriptionBilling=billing;w.SubscriptionCurrency=currency;w.SubscriptionPrice=price;w.SubscriptionActivatedUtc=DateTime.UtcNow;
        var start=effectiveDate??PakistanToday();
        w.SubscriptionRenewalDate=p.Code=="free"?null:(billing=="Annual"?start.AddYears(1):start.AddMonths(1));
        w.SubscriptionAllowanceId=Guid.NewGuid();
        w.SubscriptionReminderNoticeForDate=null;w.SubscriptionReminderEmailForDate=null;
        w.FreePositionSlots=t.PositionSlots;w.FreeRecruiterSeats=t.InternalSeats;
    }
}
