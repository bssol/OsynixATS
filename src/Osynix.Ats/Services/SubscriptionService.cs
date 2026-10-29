using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record SubscriptionOverview(EmployerWorkspace Workspace,List<SubscriptionPlan> Plans,List<EmployerAccessRequest> Requests,int Assessments,int Jds,int Slots,int Seats,SubscriptionChange? ScheduledChange=null,string CurrentPlanCode="free",int FreelancerApproaches=0,bool PaymentConfigured=false);
public record SubscriptionNotice(Guid WorkspaceId,Guid? RequestId,string Title,string Status,string Message,DateOnly? RenewalDate=null);
public class SubscriptionService(EmployerService employers,Access access,IDbContextFactory<AtsDbContext> factory,RecruitmentUpdates updates)
{
    public async Task<List<SubscriptionNotice>> Notifications()
    {
        var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        var notices=await db.EmployerAccessRequests.AsNoTracking().Where(r=>r.SubscriptionPlanId!=null&&r.AdminResponse!=""&&!db.SupportConversations.Any(c=>c.EmployerAccessRequestId==r.Id)&&db.EmployerWorkspaces.Any(w=>w.Id==r.WorkspaceId&&w.OwnerId==uid)).OrderByDescending(r=>r.RespondedUtc).Take(3).Select(r=>new SubscriptionNotice(r.WorkspaceId,r.Id,r.Feature,r.Status,"Admin sent you a message. Open Messages to review the details.")).ToListAsync();
        var reminders=await db.EmployerWorkspaces.AsNoTracking().Where(w=>w.OwnerId==uid&&w.SubscriptionRenewalDate!=null&&w.SubscriptionReminderNoticeForDate==w.SubscriptionRenewalDate&&!db.SubscriptionChanges.Any(c=>c.WorkspaceId==w.Id&&c.Status=="Scheduled")).Select(w=>new{w.Id,w.SubscriptionName,w.SubscriptionRenewalDate}).ToListAsync();
        var reminderNotices=reminders.Select(w=>new SubscriptionNotice(w.Id,null,"Subscription renewal reminder","Upcoming",$"Your {w.SubscriptionName} plan is due for renewal on {w.SubscriptionRenewalDate:dd MMM yyyy} (Pakistan time). Contact your Admin to arrange renewal; no automatic charge or access change will occur.",w.SubscriptionRenewalDate));
        return notices.Concat(reminderNotices).OrderByDescending(n=>n.RenewalDate??DateOnly.MinValue).Take(5).ToList();
    }
    public async Task<SubscriptionOverview> Overview()
    {
        var overview=await employers.Overview();var w=overview.Workspace;await using var db=await factory.CreateDbContextAsync();
        var category=SubscriptionRules.Category(w);var month=SubscriptionRules.Month(DateTime.UtcNow);
        var counts=await db.SubscriptionUsage.Where(u=>u.WorkspaceId==w.Id&&u.AllowanceId==w.SubscriptionAllowanceId&&u.MonthUtc==month).GroupBy(u=>u.Kind).Select(g=>new{Kind=g.Key,Count=g.Count()}).ToDictionaryAsync(g=>g.Kind,g=>g.Count);
        var usage=await employers.Usage();
        return new(w,await db.SubscriptionPlans.AsNoTracking().Where(p=>p.AccountCategory==category&&p.Published).OrderBy(p=>p.MonthlyPkr).ThenBy(p=>p.Code=="free"?0:p.Code=="starter"?1:p.Code=="growth"?2:3).ToListAsync(),overview.Requests,counts.GetValueOrDefault("Assessment"),counts.GetValueOrDefault("JD"),usage.UsedPositionSlots,usage.ActiveRecruiters,await db.SubscriptionChanges.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w.Id&&x.Status=="Scheduled"),await db.SubscriptionPlans.Where(p=>p.Id==w.SubscriptionPlanId).Select(p=>p.Code).SingleAsync(),await SubscriptionRules.FreelancerApproaches(db,w.Id),AdminPaymentDetailsService.IsConfigured(await AdminPaymentDetailsService.Current(db)));
    }
    public async Task Request(Guid planId,Guid revision,string billing,string currency,string note)
    {
        if(billing is not("Monthly" or "Annual")||currency is not("PKR" or "USD")||note.Length>2000)throw new InvalidOperationException("Choose Monthly or Annual, PKR or USD, and a note of at most 2,000 characters.");
        var overview=await employers.Overview();await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        var workspaceId=overview.Workspace.Id;await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmployerWorkspaces SET Id=Id WHERE Id={workspaceId}");
        var w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(x=>x.Id==workspaceId);
        var p=await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==planId&&p.Published)??throw new InvalidOperationException("This plan is not currently available.");
        if(p.Revision!=revision)throw new InvalidOperationException("The plan or its price has changed. Refresh and review before requesting.");
        if(p.Code=="free"||p.AccountCategory!=SubscriptionRules.Category(w)||p.Id==w.SubscriptionPlanId)throw new InvalidOperationException("Choose a different paid plan for your account type.");
        var current=await db.SubscriptionPlans.AsNoTracking().SingleAsync(x=>x.Id==w.SubscriptionPlanId);
        if(SubscriptionRules.Tier(p.Code)<=SubscriptionRules.Tier(current.Code))throw new InvalidOperationException("Choose Schedule downgrade to change to a lower tier at renewal.");
        if(await db.EmployerAccessRequests.AnyAsync(r=>r.WorkspaceId==w.Id&&r.SubscriptionPlanId!=null&&(r.Status=="Submitted"||r.Status=="Under Review"||r.Status=="Needs Information")))throw new InvalidOperationException("An upgrade request is already pending. View Upgrade Requests for Admin's response.");
        var bank=await AdminPaymentDetailsService.Current(db);
        if(!AdminPaymentDetailsService.IsConfigured(bank))throw new InvalidOperationException("Admin has not configured bank transfer instructions yet. Please contact Support and try again later.");
        var r=new EmployerAccessRequest{WorkspaceId=w.Id,SubscriptionPlanId=p.Id,Feature="Subscription: "+p.Name,PlanSnapshotJson=JsonSerializer.Serialize(p),Currency=currency,BillingPeriod=billing,QuotedPrice=SubscriptionRules.Price(p,billing,currency),Remarks=note.Trim()};
        db.EmployerAccessRequests.Add(r);
        var adminId=await(from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join role in db.Roles on ur.RoleId equals role.Id where role.Name=="Admin"&&u.Status=="Active" orderby u.Id select u.Id).FirstOrDefaultAsync();
        if(string.IsNullOrWhiteSpace(adminId))throw new InvalidOperationException("An Admin account is not available to receive this request.");
        var owner=await db.Users.AsNoTracking().Where(u=>u.Id==w.OwnerId).Select(u=>u.FullName).SingleAsync();
        var conversation=new SupportConversation{UserId=w.OwnerId,WorkspaceId=w.Id,EmployerAccessRequestId=r.Id,IssueType="Subscription & Payment",Subject="Subscription upgrade · "+p.Name};
        db.SupportConversations.Add(conversation);
        var body=AdminPaymentDetailsService.UpgradeMessage(r,owner,bank!);
        db.SupportMessages.Add(new SupportMessage{ConversationId=conversation.Id,SenderId=adminId,Body=body});
        conversation.LastMessageUtc=DateTime.UtcNow;
        AtsService.Audit(db,w.OwnerId,"Subscription upgrade requested",r.Id.ToString(),$"{p.AccountCategory} · {p.Name} · {billing} · {currency}");await db.SaveChangesAsync();await tx.CommitAsync();updates.Publish();
    }

    public async Task WithdrawUpgrade(Guid requestId)
    {
        var overview=await employers.Overview();
        await using var db=await factory.CreateDbContextAsync();
        await using var tx=await db.Database.BeginTransactionAsync();
        var request=await db.EmployerAccessRequests.SingleOrDefaultAsync(r=>r.Id==requestId&&r.WorkspaceId==overview.Workspace.Id&&r.SubscriptionPlanId!=null)
            ??throw new InvalidOperationException("This upgrade request is not available in your workspace.");
        if(request.Status is not("Submitted" or "Under Review" or "Needs Information"))
            throw new InvalidOperationException("Only a pending upgrade request can be withdrawn.");
        request.Status="Withdrawn";
        request.RespondedUtc=DateTime.UtcNow;
        request.AdminResponse="The account owner withdrew this upgrade request.";
        var conversation=await db.SupportConversations.SingleOrDefaultAsync(c=>c.EmployerAccessRequestId==request.Id&&c.Status=="Open");
        var ownerId=overview.Workspace.OwnerId;
        if(conversation is not null){
            db.SupportMessages.Add(new SupportMessage{ConversationId=conversation.Id,SenderId=ownerId,Body="I have withdrawn this upgrade request. Please do not activate the requested plan."});
            conversation.LastMessageUtc=DateTime.UtcNow;
            conversation.UserReadUtc=conversation.LastMessageUtc;
        }
        AtsService.Audit(db,ownerId,"Subscription upgrade withdrawn",request.Id.ToString(),request.Feature);
        await db.SaveChangesAsync();await tx.CommitAsync();updates.Publish();
    }
    public async Task<List<SubscriptionPlan>> Catalogue()
    {
        await access.RequireAdminPermission("admin.workspaces.view");await using var db=await factory.CreateDbContextAsync();return await db.SubscriptionPlans.AsNoTracking().OrderBy(p=>p.AccountCategory).ThenBy(p=>p.CreatedUtc).ToListAsync();
    }

    public async Task<SubscriptionChange?> AdminScheduledChange(Guid workspaceId)
    {
        await access.RequireAdminPermission("admin.workspaces.view");await using var db=await factory.CreateDbContextAsync();
        return await db.SubscriptionChanges.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==workspaceId&&x.Status=="Scheduled");
    }

    public async Task ScheduleChange(Guid planId,Guid planRevision,Guid workspaceRevision,string billing,string currency)
    {
        if(billing is not("Monthly" or "Annual")||currency is not("PKR" or "USD"))throw new InvalidOperationException("Choose a valid billing period and currency.");
        var owner=await employers.Overview();await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmployerWorkspaces SET Id=Id WHERE Id={owner.Workspace.Id}");
        var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==owner.Workspace.Id);
        if(workspace.Revision!=workspaceRevision)throw new DbUpdateConcurrencyException("Your subscription changed. Refresh before continuing.");
        var current=await db.SubscriptionPlans.AsNoTracking().SingleAsync(p=>p.Id==workspace.SubscriptionPlanId);
        var target=await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==planId&&p.Published&&p.AccountCategory==SubscriptionRules.Category(workspace))??throw new InvalidOperationException("This plan is unavailable for your account.");
        if(target.Revision!=planRevision)throw new DbUpdateConcurrencyException("The target plan changed. Review it again.");
        if(SubscriptionRules.Tier(target.Code)>=SubscriptionRules.Tier(current.Code))throw new InvalidOperationException("Only a lower plan can be scheduled at renewal.");
        var effective=workspace.SubscriptionRenewalDate??throw new InvalidOperationException("Ask Admin to set your renewal date before scheduling a cancellation or downgrade.");
        foreach(var old in await db.SubscriptionChanges.Where(x=>x.WorkspaceId==workspace.Id&&x.Status=="Scheduled").ToListAsync())old.Status="Replaced";
        foreach(var request in await db.EmployerAccessRequests.Where(r=>r.WorkspaceId==workspace.Id&&r.SubscriptionPlanId!=null&&(r.Status=="Submitted"||r.Status=="Under Review"||r.Status=="Needs Information")).ToListAsync()){
            request.Status="Withdrawn";request.AdminResponse="The account owner withdrew this upgrade by scheduling a cancellation or downgrade.";request.RespondedUtc=DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        var change=new SubscriptionChange{WorkspaceId=workspace.Id,PlanId=target.Id,PlanName=target.Name,PlanSnapshotJson=JsonSerializer.Serialize(target),Kind=target.Code=="free"?"Cancellation":"Downgrade",EffectiveDate=effective,BillingPeriod=billing,Currency=currency,Price=target.Code=="free"?0:SubscriptionRules.Price(target,billing,currency)};
        db.SubscriptionChanges.Add(change);
        AtsService.Audit(db,workspace.OwnerId,"Subscription change scheduled",workspace.Id.ToString(),$"{change.Kind} to {target.Name} on {effective:yyyy-MM-dd}; current access retained until then.");
        await db.SaveChangesAsync();await tx.CommitAsync();
        await SubscriptionLifecycle.ApplyDue(db,workspace.Id);updates.Publish();
    }

    public async Task KeepCurrentPlan(Guid changeId,Guid revision)
    {
        var owner=await employers.Overview();await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmployerWorkspaces SET Id=Id WHERE Id={owner.Workspace.Id}");
        var change=await db.SubscriptionChanges.SingleOrDefaultAsync(x=>x.Id==changeId&&x.WorkspaceId==owner.Workspace.Id&&x.Status=="Scheduled")??throw new InvalidOperationException("This change has already taken effect or was withdrawn. Refresh your subscription.");
        if(change.Revision!=revision)throw new DbUpdateConcurrencyException("The scheduled change was updated. Refresh first.");
        change.Status="Withdrawn";AtsService.Audit(db,owner.Workspace.OwnerId,"Scheduled subscription change withdrawn",owner.Workspace.Id.ToString(),"The current plan will continue.");
        await db.SaveChangesAsync();await tx.CommitAsync();updates.Publish();
    }
    public async Task SavePlan(SubscriptionPlan input,SubscriptionTerms terms,bool applyToSubscribers=false)
    {
        await access.RequireAdminPermission("admin.workspaces.view");
        var actor=await access.RequireAdminPermission("admin.subscriptions.manage");
        if(input.AccountCategory is not("Freelancer" or "Employer" or "Agency")||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>80||input.Description.Length>1000||input.PromotionLabel.Length>120||input.PromotionPercent<0||input.PromotionPercent>=100||(input.PromotionStartsOn is DateOnly start&&input.PromotionEndsOn is DateOnly end&&end<start))throw new InvalidOperationException("Check the account type, plan name, promotion dates and discount (0–99%).");
        if(new[]{input.MonthlyPkr,input.AnnualPkr,input.MonthlyUsd,input.AnnualUsd}.Any(n=>n<0||n>1000000000||n!=decimal.Round(n,2))||new[]{terms.PositionSlots,terms.InternalSeats,terms.MonthlyAssessments??0,terms.MonthlyJdAnalyses??0,terms.ApplicationsPerPosition??0}.Any(n=>n<0||n>1000000))throw new InvalidOperationException("Enter non-negative prices with at most two decimal places and capacity within the permitted range.");
        if(input.AccountCategory=="Freelancer"&&terms.InternalSeats!=0)throw new InvalidOperationException("Freelancer plans cannot include internal team seats.");
        if(input.Code=="free"&&new[]{input.MonthlyPkr,input.AnnualPkr,input.MonthlyUsd,input.AnnualUsd}.Any(n=>n!=0))throw new InvalidOperationException("Free plans must remain free.");
        if(input.Code=="free"&&input.PromotionPercent>0)throw new InvalidOperationException("The Free plan cannot have promotional pricing.");
        if(input.Code=="free"&&!input.Published)throw new InvalidOperationException("Keep the default Free plan published.");
        if(terms.MonthlyFreelancerApproaches is <0 or >1000000)throw new InvalidOperationException("Enter a non-negative monthly freelancer approach limit or leave it blank for unlimited.");
        await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var p=await db.SubscriptionPlans.SingleAsync(p=>p.Id==input.Id);
        if(p.Revision!=input.Revision)throw new DbUpdateConcurrencyException("This plan changed. Reload before saving.");
        if(p.AccountCategory!=input.AccountCategory||p.Code!=input.Code)throw new InvalidOperationException("The plan's account type and code cannot be changed.");
        var previous=JsonSerializer.Serialize(p);
        terms.HiringManagerReviews=p.Code!="free";
        p.Name=input.Name.Trim();p.Description=input.Description.Trim();p.Published=input.Published;p.PricingConfirmed=input.PricingConfirmed;p.MonthlyPkr=input.MonthlyPkr;p.AnnualPkr=input.AnnualPkr;p.MonthlyUsd=input.MonthlyUsd;p.AnnualUsd=input.AnnualUsd;p.PromotionPercent=input.PromotionPercent;p.PromotionLabel=input.PromotionLabel.Trim();p.PromotionStartsOn=input.PromotionPercent>0?input.PromotionStartsOn:null;p.PromotionEndsOn=input.PromotionPercent>0?input.PromotionEndsOn:null;p.TermsJson=JsonSerializer.Serialize(terms);
        var updated=0;
        if(applyToSubscribers){
            foreach(var workspace in await db.EmployerWorkspaces.Where(w=>w.SubscriptionPlanId==p.Id).ToListAsync()){
                workspace.SubscriptionTermsJson=p.TermsJson;workspace.SubscriptionName=p.Name;
                workspace.FreePositionSlots=terms.PositionSlots;workspace.FreeRecruiterSeats=terms.InternalSeats;updated++;
            }
        }
        AtsService.Audit(db,actor.FindFirstValue(ClaimTypes.NameIdentifier)!,"Subscription plan updated",p.Id.ToString(),$"{p.AccountCategory} · {p.Name} · published={p.Published} · current subscribers updated={updated}; prices, renewal dates, usage and explicit Admin grants preserved.",null,null,previous,JsonSerializer.Serialize(p));await db.SaveChangesAsync();await tx.CommitAsync();updates.Publish();
    }

    public async Task ResetFree(Guid workspaceId,Guid revision)
    {
        var actor=await access.RequireAdminPermission("admin.subscriptions.manage");await access.RequireAdminPermission("admin.workspaces.view");
        await using var db=await factory.CreateDbContextAsync();var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==workspaceId);
        if(w.Revision!=revision)throw new DbUpdateConcurrencyException("Workspace access changed. Reload first.");
        var category=SubscriptionRules.Category(w);var free=await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory==category&&p.Code=="free");
        await SubscriptionLifecycle.Supersede(db,w.Id);
        SubscriptionRules.Apply(w,free,price:0);AtsService.Audit(db,actor.FindFirstValue(ClaimTypes.NameIdentifier)!,"Subscription reset to Free",w.Id.ToString(),"Paid plan benefits revoked. Separately granted Admin capacity and features are unchanged.");await db.SaveChangesAsync();updates.Publish();
    }
}
