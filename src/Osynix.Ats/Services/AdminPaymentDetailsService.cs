using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public sealed class AdminPaymentDetailsService(
    IDbContextFactory<AtsDbContext> factory,
    Access access,
    RecruitmentUpdates updates)
{
    public static readonly Guid SettingsId = Guid.Parse("f2bc25d8-5a3d-4c32-bb22-b94e8e6a5011");

    public async Task<AdminPaymentDetails> GetForAdmin()
    {
        await access.RequireAdminPermission("admin.subscriptions.manage");
        await using var db = await factory.CreateDbContextAsync();
        var details = await db.AdminPaymentDetails.AsNoTracking().SingleOrDefaultAsync(x => x.Id == SettingsId);
        return details ?? new AdminPaymentDetails { Id = SettingsId };
    }

    public async Task Save(AdminPaymentDetails input)
    {
        var actor = await access.RequireAdminPermission("admin.subscriptions.manage");
        if (input.BankName.Length > 120 || input.AccountTitle.Length > 160 || input.AccountNumber.Length > 80 ||
            input.Iban.Length > 80 || input.Branch.Length > 160 || input.PaymentInstructions.Length > 2000)
            throw new InvalidOperationException("One or more payment detail fields are too long.");
        if (string.IsNullOrWhiteSpace(input.BankName) || string.IsNullOrWhiteSpace(input.AccountTitle) ||
            string.IsNullOrWhiteSpace(input.AccountNumber) && string.IsNullOrWhiteSpace(input.Iban))
            throw new InvalidOperationException("Enter the bank name, account title, and either an account number or IBAN.");

        await using var db = await factory.CreateDbContextAsync();
        var details = await db.AdminPaymentDetails.SingleOrDefaultAsync(x => x.Id == SettingsId);
        if (details is null)
        {
            details = new AdminPaymentDetails { Id = SettingsId };
            db.AdminPaymentDetails.Add(details);
        }
        details.BankName = input.BankName.Trim();
        details.AccountTitle = input.AccountTitle.Trim();
        details.AccountNumber = input.AccountNumber.Trim();
        details.Iban = input.Iban.Trim();
        details.Branch = input.Branch.Trim();
        details.PaymentInstructions = input.PaymentInstructions.Trim();
        var actorId = actor.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pending = await db.EmployerAccessRequests.Where(r => r.SubscriptionPlanId != null && !r.PaymentConfirmed &&
            (r.Status == "Submitted" || r.Status == "Under Review" || r.Status == "Needs Information")).ToListAsync();
        foreach (var request in pending)
        {
            var workspace = await db.EmployerWorkspaces.SingleAsync(w => w.Id == request.WorkspaceId);
            var conversation = await db.SupportConversations.SingleOrDefaultAsync(c => c.EmployerAccessRequestId == request.Id && c.Status == "Open");
            if (conversation is null)
            {
                conversation = new SupportConversation { UserId = workspace.OwnerId, WorkspaceId = workspace.Id,
                    EmployerAccessRequestId = request.Id, IssueType = "Subscription & Payment", Subject = "Subscription upgrade · " + request.Feature.Replace("Subscription: ", "", StringComparison.Ordinal) };
                db.SupportConversations.Add(conversation);
            }
            var ownerName = await db.Users.Where(u => u.Id == workspace.OwnerId).Select(u => u.FullName).SingleAsync();
            db.SupportMessages.Add(new SupportMessage { ConversationId = conversation.Id, SenderId = actorId, Body = UpgradeMessage(request, ownerName, details) });
            conversation.LastMessageUtc = DateTime.UtcNow;
            conversation.AdminReadUtc = conversation.LastMessageUtc;
        }
        AtsService.Audit(db, actorId, "Admin payment details updated", SettingsId.ToString(), "Bank transfer instructions for subscription upgrades updated.");
        await db.SaveChangesAsync();
        updates.Publish();
    }

    public static async Task<AdminPaymentDetails?> Current(AtsDbContext db) =>
        await db.AdminPaymentDetails.AsNoTracking().SingleOrDefaultAsync(x => x.Id == SettingsId);

    public static bool IsConfigured(AdminPaymentDetails? details) => details is not null &&
        !string.IsNullOrWhiteSpace(details.BankName) && !string.IsNullOrWhiteSpace(details.AccountTitle) &&
        (!string.IsNullOrWhiteSpace(details.AccountNumber) || !string.IsNullOrWhiteSpace(details.Iban));

    public static string Format(AdminPaymentDetails? details)
    {
        if (details is null) return "Bank transfer details have not been configured yet. Please reply here and Osynix Support will provide the payment instructions.";
        var lines = new List<string> { $"Bank: {details.BankName}", $"Account title: {details.AccountTitle}" };
        if (!string.IsNullOrWhiteSpace(details.AccountNumber)) lines.Add($"Account number: {details.AccountNumber}");
        if (!string.IsNullOrWhiteSpace(details.Iban)) lines.Add($"IBAN: {details.Iban}");
        if (!string.IsNullOrWhiteSpace(details.Branch)) lines.Add($"Branch: {details.Branch}");
        if (!string.IsNullOrWhiteSpace(details.PaymentInstructions)) lines.Add(details.PaymentInstructions);
        return string.Join("\n", lines);
    }

    public static string UpgradeMessage(EmployerAccessRequest request, string ownerName, AdminPaymentDetails details)
    {
        var amount = request.QuotedPrice is decimal quoted ? $"{request.Currency} {quoted:N0} ({request.BillingPeriod})" : "as agreed with Admin";
        return $"Payment instructions for your {request.Feature.Replace("Subscription: ", "", StringComparison.Ordinal)} plan upgrade\n\n" +
            $"Hello {(string.IsNullOrWhiteSpace(ownerName) ? "" : ownerName + ", ")}your request has been received. Amount due: {amount}.\n\n" +
            $"{Format(details)}\n\n" +
            "After payment, reply in this conversation and attach your payment screenshot or receipt (PDF, PNG, JPEG, or DOCX, up to 5 MB). Please submit evidence within 24 hours. " +
            "You can withdraw the request from Settings → Subscriptions → Upgrade requests. Your current plan remains active until Admin confirms payment and approves the upgrade.";
    }
}
