using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public sealed class UpgradePaymentReminderService(
    IServiceScopeFactory scopes,
    RecruitmentUpdates updates,
    ILogger<UpgradePaymentReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Remind(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await Remind(stoppingToken);
    }

    async Task Remind(CancellationToken token)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(token);
            var bank = await AdminPaymentDetailsService.Current(db);
            if (!AdminPaymentDetailsService.IsConfigured(bank)) return;

            var now = DateTime.UtcNow;
            var requests = await db.EmployerAccessRequests.Where(r => r.SubscriptionPlanId != null && !r.PaymentConfirmed &&
                (r.Status == "Submitted" || r.Status == "Under Review" || r.Status == "Needs Information")).ToListAsync(token);
            var published = false;
            foreach (var request in requests)
            {
                var conversation = await db.SupportConversations.SingleOrDefaultAsync(c => c.EmployerAccessRequestId == request.Id && c.Status == "Open", token);
                if (conversation is null) continue;
                var lastReminder = await db.SupportMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id && m.Body.StartsWith("Payment reminder:"))
                    .OrderByDescending(m => m.CreatedUtc).Select(m => (DateTime?)m.CreatedUtc).FirstOrDefaultAsync(token);
                var latest = await db.SupportMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id)
                    .OrderByDescending(m => m.CreatedUtc).Select(m => new { m.SenderId, m.AttachmentName, m.Body, m.CreatedUtc }).FirstOrDefaultAsync(token);
                var dueFrom = lastReminder ?? request.CreatedUtc;
                if (latest is not null && !latest.Body.StartsWith("Payment reminder:") && latest.CreatedUtc > dueFrom) dueFrom = latest.CreatedUtc;
                if (dueFrom > now.AddHours(-24)) continue;

                // Once a user submits a receipt, pause reminders while Admin reviews it.
                if (latest is not null && latest.SenderId == conversation.UserId && latest.AttachmentName.Length > 0 &&
                    (lastReminder is null || latest.CreatedUtc > lastReminder.Value)) continue;

                var sender = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join role in db.Roles on ur.RoleId equals role.Id
                    where role.Name == "Admin" && u.Status == "Active" orderby u.Id select u.Id).FirstOrDefaultAsync(token);
                if (string.IsNullOrWhiteSpace(sender)) continue;
                db.SupportMessages.Add(new SupportMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = sender,
                    Body = $"Payment reminder: we have not yet marked payment as confirmed for {request.Feature}. Please reply in this conversation with your payment receipt within 24 hours, or withdraw the request from Settings → Subscriptions → Upgrade requests. Your current plan remains active until Admin confirms payment."
                });
                conversation.LastMessageUtc = now;
                conversation.AdminReadUtc = now;
                published = true;
            }
            if (published) { await db.SaveChangesAsync(token); updates.Publish(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Subscription upgrade payment reminder cycle failed."); }
    }
}
