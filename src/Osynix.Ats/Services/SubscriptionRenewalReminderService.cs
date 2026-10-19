using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;

namespace Osynix.Ats.Services;

public sealed class SubscriptionRenewalReminderService(
    IServiceScopeFactory scopes,
    RecruitmentUpdates updates,
    ILogger<SubscriptionRenewalReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Remind(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await Remind(stoppingToken);
    }

    async Task Remind(CancellationToken token)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtsDbContext>>();
            var mail = scope.ServiceProvider.GetRequiredService<IAccountEmail>();
            var today = SubscriptionRules.PakistanToday();
            await using var db = await factory.CreateDbContextAsync(token);
            var workspaces = await db.EmployerWorkspaces.Where(w =>
                w.SubscriptionPlanId != null && w.SubscriptionName != "Free" &&
                w.SubscriptionRenewalDate != null && w.SubscriptionRenewalDate > today &&
                w.SubscriptionRenewalDate <= today.AddDays(7)&&!db.SubscriptionChanges.Any(x=>x.WorkspaceId==w.Id&&x.Status=="Scheduled")).ToListAsync(token);
            var published = false;

            foreach (var workspace in workspaces)
            {
                var renewal = workspace.SubscriptionRenewalDate!.Value;
                if (workspace.SubscriptionReminderNoticeForDate != renewal)
                {
                    workspace.SubscriptionReminderNoticeForDate = renewal;
                    await db.SaveChangesAsync(token);
                    published = true;
                }

                if (workspace.SubscriptionReminderEmailForDate == renewal) continue;
                var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == workspace.OwnerId, token);
                if (owner?.Email is not { Length: > 0 } address) continue;
                var days = renewal.DayNumber - today.DayNumber;
                var subject = $"Your Osynix ATS {workspace.SubscriptionName} plan renews in {days} day{(days == 1 ? "" : "s")}";
                var body = $"<p>Hello {System.Net.WebUtility.HtmlEncode(owner.FullName)},</p>" +
                    $"<p>Your <b>{System.Net.WebUtility.HtmlEncode(workspace.SubscriptionName)}</b> plan is due for renewal on <b>{renewal:dd MMM yyyy}</b> (Pakistan time).</p>" +
                    "<p>Please contact your Admin to arrange renewal. No automatic payment or access change will occur.</p>" +
                    "<p>Regards,<br>Osynix ATS</p>";
                try
                {
                    await mail.SendHtml(address, subject, body);
                    workspace.SubscriptionReminderEmailForDate = renewal;
                    await db.SaveChangesAsync(token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not send renewal reminder for workspace {WorkspaceId}.", workspace.Id);
                }
            }
            if (published) updates.Publish();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Subscription renewal reminder cycle failed."); }
    }
}
