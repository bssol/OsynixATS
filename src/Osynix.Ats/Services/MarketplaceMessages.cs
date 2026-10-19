using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record ConversationSummary(Guid Id, string Name, string Context, string Preview, DateTime UpdatedUtc, int Unread, string InvitationStatus);
public record ConversationMessage(Guid Id, string SenderId, string SenderName, string Body, DateTime SentUtc, ProjectInvitation? Invitation, string AttachmentName);
public record ConversationDetail(Guid Id, string Name, string Company, string FreelancerId, Guid WorkspaceId, bool Employer, bool Agency, List<ConversationMessage> Messages);
public record ProjectInviteDraft(Guid? PositionId, string Title, string Summary, string Note, string Start, string Duration, bool NotifyEmail);

/// <summary>Account-level conversations, explicitly scoped to the two participants, independently of the selected workspace.</summary>
public class MarketplaceMessages(AuthenticationStateProvider auth, IDbContextFactory<AtsDbContext> factory, RecruitmentUpdates updates, IAccountEmail mail, IConfiguration config)
{
    async Task<AtsUser> Current(AtsDbContext db)
    {
        var principal = (await auth.GetAuthenticationStateAsync()).User;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || !(principal.IsInRole("Employer") || principal.IsInRole("Recruiter"))) throw new UnauthorizedAccessException("Sign in to your Employer or Freelancer account.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Status == "Active" && u.EmailConfirmed) ?? throw new UnauthorizedAccessException("Your account is not active or verified.");
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == id select r.Name!).ToListAsync();
        if (!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Order())) throw new UnauthorizedAccessException("Your access changed. Sign in again.");
        return user;
    }

    async Task<(AtsUser User, EmployerWorkspace Workspace, bool Agency)> Owner(AtsDbContext db)
    {
        var user = await Current(db);
        var principal = (await auth.GetAuthenticationStateAsync()).User;
        var active = await WorkspaceContext.Resolve(db, principal);
        var workspace = await db.EmployerWorkspaces.SingleOrDefaultAsync(w => w.Id == active.Id && w.OwnerId == user.Id && w.Kind != AccountTypes.Freelance) ?? throw new UnauthorizedAccessException("Only an Employer or Agency workspace owner can invite freelancers.");
        var agency = await ProfileModes.Selected(db, principal, active) == true;
        return (user, workspace, agency);
    }

    async Task<(MarketplaceConversation Conversation, EmployerWorkspace Workspace, AtsUser User)> Participant(AtsDbContext db, Guid id)
    {
        var user = await Current(db);
        var conversation = await db.MarketplaceConversations.SingleOrDefaultAsync(c => c.Id == id) ?? throw new UnauthorizedAccessException("Conversation unavailable.");
        var workspace = await db.EmployerWorkspaces.SingleAsync(w => w.Id == conversation.WorkspaceId);
        if (user.Id != conversation.FreelancerId && user.Id != workspace.OwnerId) throw new UnauthorizedAccessException("This conversation belongs to another account.");
        if (!await WorkspaceContext.OwnerActive(db, workspace.OwnerId)) throw new UnauthorizedAccessException("This Employer account is inactive.");
        return (conversation, workspace, user);
    }

    public async Task<List<ConversationSummary>> Inbox()
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await Current(db);
        var rows = await (from c in db.MarketplaceConversations.AsNoTracking()
                          join w in db.EmployerWorkspaces.AsNoTracking() on c.WorkspaceId equals w.Id
                          join f in db.Users.AsNoTracking() on c.FreelancerId equals f.Id
                          where (c.FreelancerId == user.Id || w.OwnerId == user.Id)
                          && !db.ChatInboxDeletions.Any(d=>d.UserId==user.Id&&d.Kind=="Project"&&d.ConversationId==c.Id&&d.DeletedThroughUtc>=c.LastMessageUtc)
                          && (w.OwnerId == user.Id || db.MarketplaceMessages.Any(m => m.ConversationId == c.Id))
                          && db.Users.Any(o => o.Id == w.OwnerId && o.Status == "Active")
                          orderby c.LastMessageUtc descending
                          select new {
                              c.Id, Name = w.OwnerId == user.Id ? f.FullName : w.CompanyName, c.LastMessageUtc,
                              Context = db.ProjectInvitations.Where(i=>i.ConversationId==c.Id).OrderByDescending(i=>i.CreatedUtc).Select(i=>i.Title).FirstOrDefault(),
                              Status = db.ProjectInvitations.Where(i=>i.ConversationId==c.Id).OrderByDescending(i=>i.CreatedUtc).Select(i=>i.Status).FirstOrDefault(),
                              Preview = db.MarketplaceMessages.Where(m=>m.ConversationId==c.Id).OrderByDescending(m=>m.CreatedUtc).Select(m=>m.Body==""?m.AttachmentName:m.Body).FirstOrDefault(),
                              Unread = db.MarketplaceMessages.Count(m=>m.ConversationId==c.Id&&m.SenderId!=user.Id&&(w.OwnerId==user.Id?(c.EmployerReadUtc==null||m.CreatedUtc>c.EmployerReadUtc):(c.FreelancerReadUtc==null||m.CreatedUtc>c.FreelancerReadUtc)))
                          }).ToListAsync();
        return rows.Select(r=>new ConversationSummary(r.Id,r.Name,r.Context??"Project discussion",r.Preview??"Start a conversation",r.LastMessageUtc,r.Unread,r.Status??"")).ToList();
    }

    public async Task<int> Unread() => (await Inbox()).Sum(c => c.Unread);

    public async Task DeleteChat(Guid id)
    {
        await using var db=await factory.CreateDbContextAsync();
        var(c,_,user)=await Participant(db,id);
        var deletion=await db.ChatInboxDeletions.FindAsync(user.Id,"Project",id);
        if(deletion is null){deletion=new(){UserId=user.Id,Kind="Project",ConversationId=id};db.ChatInboxDeletions.Add(deletion);}
        deletion.DeletedThroughUtc=c.LastMessageUtc;
        AtsService.Audit(db,user.Id,"Project chat deleted from inbox",id.ToString());
        await db.SaveChangesAsync();updates.Publish();
    }

    public async Task<ConversationDetail> Open(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var (c, w, user) = await Participant(db, id);
        var employer = w.OwnerId == user.Id;
        var other = await db.Users.AsNoTracking().SingleAsync(u => u.Id == (employer ? c.FreelancerId : w.OwnerId));
        var invitations = await db.ProjectInvitations.AsNoTracking().Where(i => i.ConversationId == c.Id).ToDictionaryAsync(i => i.Id);
        // Attachment bytes are only loaded by the individually authorised download endpoint.
        var messages = await db.MarketplaceMessages.AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.CreatedUtc).Select(m => new { m.Id, m.SenderId, m.Body, m.CreatedUtc, m.InvitationId, m.AttachmentName }).ToListAsync();
        var detail = new ConversationDetail(c.Id, employer ? other.FullName : w.CompanyName, w.CompanyName, c.FreelancerId, w.Id, employer, c.Agency,
            messages.Select(m => new ConversationMessage(m.Id, m.SenderId, m.SenderId == user.Id ? "You" : employer ? other.FullName : w.CompanyName, m.Body, m.CreatedUtc, m.InvitationId is Guid inviteId ? invitations.GetValueOrDefault(inviteId) : null, m.AttachmentName)).ToList());
        var readThrough = messages.LastOrDefault()?.CreatedUtc ?? c.LastMessageUtc;
        var previousRead = employer ? c.EmployerReadUtc : c.FreelancerReadUtc;
        if (employer) c.EmployerReadUtc = readThrough; else c.FreelancerReadUtc = readThrough;
        await db.SaveChangesAsync();
        if (previousRead == null || previousRead < readThrough) updates.Publish();
        return detail;
    }

    public async Task<Guid> Start(string freelancerId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var (user, workspace, agency) = await Owner(db);
        await SubscriptionRules.RequireMarketplace(db,freelancerId);
        if (!await db.Users.AnyAsync(u => u.Id == freelancerId && u.AccountType == AccountTypes.Freelance && u.EmailConfirmed && u.Status == "Active")) throw new InvalidOperationException("This freelancer is no longer available.");
        var existing = await db.MarketplaceConversations.SingleOrDefaultAsync(c => c.WorkspaceId == workspace.Id && c.FreelancerId == freelancerId && c.Agency == agency);
        if (existing is not null) return existing.Id;

        var conversation = new MarketplaceConversation { WorkspaceId = workspace.Id, FreelancerId = freelancerId, Agency = agency, EmployerReadUtc = DateTime.UtcNow };
        db.Add(conversation);
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateException){db.ChangeTracker.Clear();var raced=await db.MarketplaceConversations.SingleOrDefaultAsync(c=>c.WorkspaceId==workspace.Id&&c.FreelancerId==freelancerId&&c.Agency==agency);if(raced is null)throw;return raced.Id;}
        updates.Publish();
        return conversation.Id;
    }

    public async Task Send(Guid id, string body, string fileName = "", byte[]? content = null)
    {
        body = body.Trim();
        if (body.Length > 10000 || (body.Length == 0 && content is not { Length: > 0 })) throw new InvalidOperationException("Write a message of up to 10,000 characters or attach a file.");
        var type = ValidateAttachment(fileName, content);
        await using var db = await factory.CreateDbContextAsync();
        var (c, w, user) = await Participant(db, id);
        var otherId = user.Id == c.FreelancerId ? w.OwnerId : c.FreelancerId;
        if (!await db.Users.AnyAsync(u => u.Id == otherId && u.Status == "Active")) throw new InvalidOperationException("The recipient's account is inactive.");
        AddMessage(db, c, user.Id, body, null, fileName, type, content);
        await db.SaveChangesAsync();
        updates.Publish();
    }

    static string ValidateAttachment(string name, byte[]? content)
    {
        if (content is not { Length: > 0 }) return "";
        if (content.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Attachments must be 5 MB or smaller.");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch {
            ".pdf" when content.AsSpan().StartsWith("%PDF"u8) => "application/pdf",
            ".png" when content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
            ".jpg" or ".jpeg" when content.Length > 3 && content[0] == 255 && content[1] == 216 && content[2] == 255 => "image/jpeg",
            ".docx" when content.Length > 4 && content[0] == 80 && content[1] == 75 && content[2] == 3 && content[3] == 4 => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => throw new InvalidOperationException("Attach a PDF, DOCX, PNG or JPEG file.")
        };
    }

    public async Task<Guid> Invite(string freelancerId, ProjectInviteDraft draft)
    {
        if (draft.Summary.Trim().Length is < 1 or > 5000 || draft.Note.Length > 10000 || draft.Start.Length > 200 || draft.Duration.Length > 200) throw new InvalidOperationException("Add a project summary and review the invitation field lengths.");
        var id = await Start(freelancerId);
        await using var db = await factory.CreateDbContextAsync();
        var (c, w, user) = await Participant(db, id);
        // Owner is verified again, including the currently selected working profile.
        var owner = await Owner(db);
        if (owner.Workspace.Id != c.WorkspaceId || owner.Agency != c.Agency) throw new UnauthorizedAccessException();
        var title = draft.Title.Trim();
        if (draft.PositionId is Guid pid) {
            var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == pid && p.WorkspaceId == w.Id && p.IsAgencyPosition == c.Agency && (p.Status == "Active" || p.Status == "On Hold")) ?? throw new InvalidOperationException("Select an open position from this workspace and working profile.");
            title = $"{position.Title} · #{position.Reference}";
        }
        if (title.Length is < 1 or > 250) throw new InvalidOperationException("Enter a project title or select a position.");
        if (await db.ProjectInvitations.AnyAsync(i => i.ConversationId == id && i.Status == "Pending" && ((draft.PositionId != null && i.PositionId == draft.PositionId) || (draft.PositionId == null && i.Title == title)))) throw new InvalidOperationException("An invitation for this assignment is already awaiting a response. Continue the conversation instead.");
        var invite = new ProjectInvitation { ConversationId = id, PositionId = draft.PositionId, Title = title, Summary = draft.Summary.Trim(), Start = draft.Start.Trim(), Duration = draft.Duration.Trim(), EmailDelivery = draft.NotifyEmail ? "Pending" : "Not requested" };
        db.Add(invite);
        AddMessage(db, c, user.Id, draft.Note.Trim(), invite.Id);
        AtsService.Audit(db, user.Id, "Project invitation sent", invite.Id.ToString(), title);
        await db.SaveChangesAsync();
        updates.Publish();
        if (draft.NotifyEmail) {
            var recipient = await db.Users.SingleAsync(u => u.Id == freelancerId);
            try {
                var baseUrl = config["Review:PublicBaseUrl"];
                if (!mail.Configured || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback) throw new InvalidOperationException();
                await mail.Send(recipient.Email!, $"Project invitation from {w.CompanyName}", $"Hello {recipient.FullName},\n\n{w.CompanyName} invited you to discuss {title}.\n\nSign in to your Osynix account to review the project and respond:\n{uri.GetLeftPart(UriPartial.Authority)}/messages?conversation={id}\n\nYour invitation is available in Messages.");
                invite.EmailDelivery = "Email sent";
            } catch { invite.EmailDelivery = "Email unavailable — invitation delivered in Messages"; }
            await db.SaveChangesAsync();
        }
        return id;
    }

    public async Task Respond(Guid invitationId, Guid revision, string response, string note = "")
    {
        if (response is not ("Accepted" or "Declined" or "Question" or "Cancelled")) throw new InvalidOperationException("Choose an invitation response.");
        if (note.Length > 10000 || (response == "Question" && string.IsNullOrWhiteSpace(note))) throw new InvalidOperationException("Add a question of up to 10,000 characters.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var invite = await db.ProjectInvitations.SingleOrDefaultAsync(i => i.Id == invitationId) ?? throw new UnauthorizedAccessException();
        var (c, w, user) = await Participant(db, invite.ConversationId);
        if ((response == "Cancelled" ? w.OwnerId : c.FreelancerId) != user.Id) throw new UnauthorizedAccessException("Only the invited freelancer can respond, and only the Employer can cancel.");
        if (invite.Revision != revision) throw new DbUpdateConcurrencyException();
        if (invite.Status != "Pending") throw new InvalidOperationException("This invitation has already been answered or cancelled. Refresh the conversation.");
        if (response == "Accepted") {
            if(c.Agency && !w.AgencyEnabled) throw new InvalidOperationException("Agency access is no longer enabled for this Employer. Ask them to update the assignment.");
            var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(m => m.WorkspaceId == w.Id && m.UserId == user.Id);
            if (membership?.Status == "Inactive") throw new InvalidOperationException("Your team access was deactivated. Ask the Employer to restore it before accepting.");
            Position? position = null;
            if (invite.PositionId is Guid pid) position = await db.Positions.SingleOrDefaultAsync(p => p.Id == pid && p.WorkspaceId == w.Id && p.IsAgencyPosition == c.Agency && (p.Status == "Active" || p.Status == "On Hold")) ?? throw new InvalidOperationException("This position is no longer open. Ask the Employer to update the assignment.");
            if (membership is null) db.WorkspaceMemberships.Add(new() { WorkspaceId = w.Id, UserId = user.Id });
            if (position is not null && position.AssignedRecruiter != user.UserName && !await db.PositionRecruiterAssignments.AnyAsync(a => a.PositionId == position.Id && a.UserId == user.Id)) db.PositionRecruiterAssignments.Add(new() { PositionId = position.Id, UserId = user.Id });
            var legacyInvites = await db.EmployerInvitations.Where(i => i.WorkspaceId == w.Id && i.NormalizedEmail == user.NormalizedEmail && i.Status == "Pending").ToListAsync();
            foreach (var legacy in legacyInvites) { legacy.Status = "Accepted"; legacy.AcceptedBy = user.Id; }
        }
        if (response != "Question") { invite.Status = response; invite.RespondedUtc = DateTime.UtcNow; }
        AddMessage(db, c, user.Id, response == "Question" ? note.Trim() : $"Invitation {response.ToLowerInvariant()}: {invite.Title}" + (string.IsNullOrWhiteSpace(note) ? "" : "\n" + note.Trim()));
        AtsService.Audit(db, user.Id, "Project invitation " + response.ToLowerInvariant(), invite.Id.ToString(), invite.Title);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        updates.Publish();
    }

    static void AddMessage(AtsDbContext db, MarketplaceConversation c, string sender, string body, Guid? invitationId = null, string name = "", string type = "", byte[]? content = null)
    {
        var message = new MarketplaceMessage { ConversationId = c.Id, SenderId = sender, Body = body, InvitationId = invitationId, AttachmentName = content is null ? "" : Path.GetFileName(name)[..Math.Min(Path.GetFileName(name).Length, 200)], AttachmentType = type, AttachmentContent = content };
        db.Add(message);
        c.LastMessageUtc = message.CreatedUtc;
    }

    public async Task<List<Position>> Positions()
    {
        await using var db = await factory.CreateDbContextAsync();
        var (_, w, agency) = await Owner(db);
        return await db.Positions.AsNoTracking().Where(p => p.WorkspaceId == w.Id && p.IsAgencyPosition == agency && (p.Status == "Active" || p.Status == "On Hold")).OrderBy(p => p.Title).ToListAsync();
    }

    public static async Task<MarketplaceMessage?> Download(AtsDbContext db, ClaimsPrincipal principal, Guid id)
    {
        var uid = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || !(principal.IsInRole("Employer")||principal.IsInRole("Recruiter")) || !await db.Users.AnyAsync(u => u.Id == uid && u.Status == "Active" && u.EmailConfirmed)) return null;
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==uid select r.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(c=>c.Value).Order()))return null;
        return await (from m in db.MarketplaceMessages.AsNoTracking() join c in db.MarketplaceConversations on m.ConversationId equals c.Id join w in db.EmployerWorkspaces on c.WorkspaceId equals w.Id
                      where m.Id == id && (c.FreelancerId == uid || w.OwnerId == uid) && db.Users.Any(u => u.Id == w.OwnerId && u.Status == "Active") select m).SingleOrDefaultAsync();
    }
}
