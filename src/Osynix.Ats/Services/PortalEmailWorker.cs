using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Osynix.Ats.Data;

namespace Osynix.Ats.Services;

public class PortalEmailWorker(IServiceScopeFactory scopes,ILogger<PortalEmailWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        do{try{await Deliver(stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}catch(Exception ex){logger.LogWarning(ex,"Client portal email delivery deferred");}}while(await timer.WaitForNextTickAsync(stoppingToken));
    }
    public async Task Deliver(CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtsDbContext>>();var mail=scope.ServiceProvider.GetRequiredService<IAccountEmail>();var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if(!mail.Configured)return;
        await using var db=await factory.CreateDbContextAsync(ct);
        var publicAddress=Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!uri.IsLoopback;
        var pending=await db.PortalEmailDeliveries.Where(d=>d.Status=="Pending"&&d.NextAttemptUtc<=DateTime.UtcNow&&(!d.RequiresPublicAddress||publicAddress)).OrderBy(d=>d.CreatedUtc).Take(20).ToListAsync(ct);
        foreach(var delivery in pending)
        {
            var body=delivery.Body;
            if(delivery.GrantId is Guid id){var grant=await db.HiringReviewGrants.SingleOrDefaultAsync(g=>g.Id==id,ct);if(grant is null||grant.Revoked||grant.ExpiresUtc<=DateTime.UtcNow||!delivery.Key.Contains(grant.TokenHash,StringComparison.Ordinal)){delivery.Status="Cancelled";continue;}var workspace=await db.Clients.Where(c=>c.Id==grant.ClientId).Select(c=>c.WorkspaceId).FirstOrDefaultAsync(ct);if(workspace is Guid wid&&!await SubscriptionRules.PaidPortal(db,await db.EmployerWorkspaces.SingleAsync(w=>w.Id==wid,ct)))continue;
                var token=scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Osynix.HiringReview.Token.v1").Unprotect(grant.ProtectedToken);
                var link=uri!.GetLeftPart(UriPartial.Authority)+"/review/access?token="+Uri.EscapeDataString(token)+(grant.PositionId is Guid pid?"&position="+pid:"");body=body.Replace("[PortalAccessLink]",link,StringComparison.Ordinal);
            }
            try{await mail.Send(delivery.Email,delivery.Subject,body);delivery.Status="Sent";delivery.SentUtc=DateTime.UtcNow;}
            catch(Exception ex){delivery.Attempts++;delivery.NextAttemptUtc=DateTime.UtcNow.AddMinutes(Math.Min(60,5*delivery.Attempts));logger.LogWarning(ex,"Client portal email {DeliveryId} will be retried",delivery.Id);}
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
