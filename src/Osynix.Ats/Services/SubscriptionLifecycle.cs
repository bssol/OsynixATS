using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public static class SubscriptionLifecycle
{
    public static async Task Supersede(AtsDbContext db,Guid workspaceId)
    {
        foreach(var item in await db.SubscriptionChanges.Where(x=>x.WorkspaceId==workspaceId&&x.Status=="Scheduled").ToListAsync())item.Status="Superseded";
    }

    public static async Task<bool> ApplyDue(AtsDbContext db,Guid? workspaceId=null,DateOnly? today=null,CancellationToken ct=default)
    {
        var date=today??SubscriptionRules.PakistanToday();
        var ids=await db.SubscriptionChanges.AsNoTracking().Where(x=>x.Status=="Scheduled"&&x.EffectiveDate<=date&&(workspaceId==null||x.WorkspaceId==workspaceId)).Select(x=>x.WorkspaceId).ToListAsync(ct);
        var applied=false;
        foreach(var id in ids){
            var owns=db.Database.CurrentTransaction is null;
            await using var tx=owns?await db.Database.BeginTransactionAsync(ct):null;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EmployerWorkspaces SET Id=Id WHERE Id={id}",ct);
            var change=await db.SubscriptionChanges.SingleOrDefaultAsync(x=>x.WorkspaceId==id&&x.Status=="Scheduled"&&x.EffectiveDate<=date,ct);
            if(change is null){if(tx is not null)await tx.CommitAsync(ct);continue;}
            var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==id,ct);
            await db.Entry(workspace).ReloadAsync(ct);
            var plan=JsonSerializer.Deserialize<SubscriptionPlan>(change.PlanSnapshotJson)??throw new InvalidOperationException("Scheduled plan details are unavailable.");
            if(plan.Id!=change.PlanId||plan.AccountCategory!=SubscriptionRules.Category(workspace))throw new InvalidOperationException("The scheduled plan does not match the account type. Admin review is required.");
            SubscriptionRules.Apply(workspace,plan,change.BillingPeriod,change.Currency,change.Price,change.EffectiveDate);
            change.Status="Applied";change.AppliedUtc=DateTime.UtcNow;
            AtsService.Audit(db,workspace.OwnerId,"Scheduled subscription change applied",workspace.Id.ToString(),$"{change.Kind} to {plan.Name}; effective {change.EffectiveDate:yyyy-MM-dd}; unused AI allowance discarded.");
            await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);applied=true;
        }
        return applied;
    }
}

public sealed class SubscriptionLifecycleWorker(IDbContextFactory<AtsDbContext> factory,RecruitmentUpdates updates,ILogger<SubscriptionLifecycleWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        do{
            try{await using var db=await factory.CreateDbContextAsync(stoppingToken);if(await SubscriptionLifecycle.ApplyDue(db,ct:stoppingToken))updates.Publish();}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception error){logger.LogError(error,"Scheduled subscription processing failed; it will be retried.");}
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
