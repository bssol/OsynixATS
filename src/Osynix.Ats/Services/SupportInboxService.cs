using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record SupportConversationSummary(Guid Id,string Name,string Context,string Preview,DateTime UpdatedUtc,int Unread,string Status,string AccountType,Guid? RequestId,string UserId,DateTime StartedUtc,string IssueType,DateTime? ClosedUtc);
public record SupportInboxGroup(string UserId,string Name,Guid LatestSessionId,string Preview,DateTime UpdatedUtc,int Unread,int Sessions);
public record SupportRequestInformation(Guid RequestId,Guid SessionId,DateTime SubmittedUtc,List<SupportMessageView> Messages);
public record SupportMessageView(Guid Id,string SenderId,string SenderName,string Body,DateTime SentUtc,string AttachmentName);
public record SupportConversationDetail(Guid Id,string Subject,string UserId,string UserName,string AccountType,string Status,Guid? RequestId,List<SupportMessageView> Messages,string IssueType,DateTime StartedUtc,DateTime? ClosedUtc);
public record SupportUserOption(string Id,string Name,string Email,string AccountType);

/// <summary>Always-available account support inbox, including Admin follow-up on workspace requests.</summary>
public class SupportInboxService(AuthenticationStateProvider auth,IDbContextFactory<AtsDbContext> factory,Access access,RecruitmentUpdates updates)
{
    public static readonly string[] IssueTypes=["General Support","Subscription & Payment","Account & Access","Technical Issue","Other"];
    public static List<SupportInboxGroup> GroupInbox(IEnumerable<SupportConversationSummary> sessions)=>sessions.GroupBy(c=>c.UserId).Select(g=>{var latest=g.OrderByDescending(c=>c.Preview!="No messages yet").ThenByDescending(c=>c.UpdatedUtc).First();return new SupportInboxGroup(g.Key,latest.Name,latest.Id,latest.Preview,latest.UpdatedUtc,g.Sum(c=>c.Unread),g.Count());}).OrderByDescending(c=>c.UpdatedUtc).ToList();
    async Task<(AtsUser User,bool AdminSide)> Current(AtsDbContext db)
    {
        var principal=(await auth.GetAuthenticationStateAsync()).User;
        var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if(principal.Identity?.IsAuthenticated!=true||string.IsNullOrWhiteSpace(id))throw new UnauthorizedAccessException("Sign in to access Messages.");
        var user=await db.Users.SingleOrDefaultAsync(u=>u.Id==id&&u.Status=="Active")??throw new UnauthorizedAccessException("This account is inactive.");
        var roles=await(from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==id select r.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(c=>c.Value).Order()))throw new UnauthorizedAccessException("Your access changed. Sign in again.");
        var adminSide=principal.IsInRole("Admin")||principal.IsInRole("SubAdmin");
        if(adminSide&&principal.IsInRole("SubAdmin")&&!principal.IsInRole("Admin"))await access.RequireAdminPermission("admin.requests.review");
        return(user,adminSide);
    }

    public async Task<List<SupportConversationSummary>> Inbox()
    {
        await using var db=await factory.CreateDbContextAsync();
        var (user,adminSide)=await Current(db);
        var rows=await(from c in db.SupportConversations.AsNoTracking()
                       join u in db.Users.AsNoTracking() on c.UserId equals u.Id
                       where (adminSide||(c.UserId==user.Id&&c.Status=="Open"))&&!db.ChatInboxDeletions.Any(d=>d.UserId==user.Id&&d.Kind=="Support"&&d.ConversationId==c.Id&&d.DeletedThroughUtc>=c.LastMessageUtc)
                       orderby c.LastMessageUtc descending
                       select new{Conversation=c,UserName=u.FullName,Email=u.Email,AccountType=u.AccountType,
                           Preview=db.SupportMessages.Where(m=>m.ConversationId==c.Id).OrderByDescending(m=>m.CreatedUtc).Select(m=>m.Body==""?m.AttachmentName:m.Body).FirstOrDefault(),
                           Unread=db.SupportMessages.Count(m=>m.ConversationId==c.Id&&m.SenderId!=(adminSide?user.Id:c.UserId)&&(adminSide?(c.AdminReadUtc==null||m.CreatedUtc>c.AdminReadUtc):(c.UserReadUtc==null||m.CreatedUtc>c.UserReadUtc)))})
                       .ToListAsync();
        return rows.Select(x=>new SupportConversationSummary(x.Conversation.Id,adminSide?DisplayName(x.UserName,x.Email):"Osynix Support",x.Conversation.EmployerAccessRequestId is null?x.Conversation.Subject:$"Upgrade request · {x.Conversation.Subject}",x.Preview??"No messages yet",x.Conversation.LastMessageUtc,x.Unread,x.Conversation.Status,x.AccountType,x.Conversation.EmployerAccessRequestId,x.Conversation.UserId,x.Conversation.CreatedUtc,x.Conversation.IssueType,x.Conversation.ClosedUtc)).ToList();
    }

    public async Task<int> Unread()=>(await Inbox()).Sum(x=>x.Unread);

    public async Task DeleteChat(Guid id)
    {
        await using var db=await factory.CreateDbContextAsync();var(user,adminSide)=await Current(db);
        var session=await db.SupportConversations.AsNoTracking().SingleOrDefaultAsync(c=>c.Id==id&&(adminSide||c.UserId==user.Id))??throw new UnauthorizedAccessException("Conversation unavailable.");
        var sessions=await db.SupportConversations.AsNoTracking().Where(c=>c.UserId==session.UserId).ToListAsync();
        foreach(var item in sessions){var deletion=await db.ChatInboxDeletions.FindAsync(user.Id,"Support",item.Id);if(deletion is null){deletion=new(){UserId=user.Id,Kind="Support",ConversationId=item.Id};db.ChatInboxDeletions.Add(deletion);}deletion.DeletedThroughUtc=item.LastMessageUtc;}
        AtsService.Audit(db,user.Id,"Support chat deleted from inbox",session.Id.ToString(),"Account support history retained.");
        await db.SaveChangesAsync();updates.Publish();
    }

    public async Task<List<SupportConversationSummary>> AccountHistory(Guid workspaceId)
    {
        await access.RequireAdminPermission("admin.workspaces.view");
        await using var db=await factory.CreateDbContextAsync();
        var owner=await db.EmployerWorkspaces.AsNoTracking().Where(w=>w.Id==workspaceId).Select(w=>w.OwnerId).SingleAsync();
        var sessions=await db.SupportConversations.AsNoTracking().Where(c=>c.UserId==owner).OrderByDescending(c=>c.LastMessageUtc).ToListAsync();
        return sessions.Select(c=>new SupportConversationSummary(c.Id,"Support",c.Subject,"",c.LastMessageUtc,0,c.Status,"",c.EmployerAccessRequestId,c.UserId,c.CreatedUtc,c.IssueType,c.ClosedUtc)).ToList();
    }

    public async Task<SupportConversationDetail> AccountSession(Guid workspaceId,Guid sessionId)
    {
        await access.RequireAdminPermission("admin.workspaces.view");
        await using var db=await factory.CreateDbContextAsync();
        var owner=await db.EmployerWorkspaces.AsNoTracking().Where(w=>w.Id==workspaceId).Select(w=>w.OwnerId).SingleAsync();
        var session=await db.SupportConversations.AsNoTracking().SingleOrDefaultAsync(c=>c.Id==sessionId&&c.UserId==owner)??throw new UnauthorizedAccessException("This session belongs to another account.");
        var user=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==owner);
        var messages=await(from m in db.SupportMessages.AsNoTracking() join sender in db.Users.AsNoTracking() on m.SenderId equals sender.Id where m.ConversationId==sessionId orderby m.CreatedUtc,m.Id select new SupportMessageView(m.Id,m.SenderId,sender.FullName==""?sender.Email??"Account user":sender.FullName,m.Body,m.CreatedUtc,m.AttachmentName)).ToListAsync();
        return new(session.Id,session.Subject,owner,DisplayName(user.FullName,user.Email),user.AccountType,session.Status,session.EmployerAccessRequestId,messages,session.IssueType,session.CreatedUtc,session.ClosedUtc);
    }

    public async Task EndSession(Guid id)
    {
        await using var db=await factory.CreateDbContextAsync();var(user,adminSide)=await Current(db);
        var session=await db.SupportConversations.SingleOrDefaultAsync(c=>c.Id==id&&(adminSide||c.UserId==user.Id))??throw new UnauthorizedAccessException("Session unavailable.");
        if(session.Status=="Closed")return;
        session.Status="Closed";session.ClosedUtc=DateTime.UtcNow;
        AtsService.Audit(db,user.Id,"Support session ended",session.Id.ToString(),session.IssueType+" · "+session.Subject);
        await db.SaveChangesAsync();updates.Publish();
    }

    public async Task<Dictionary<Guid,SupportRequestInformation>> RequestInformation(Guid workspaceId)
    {
        await access.RequireAdminPermission("admin.workspaces.view");await access.RequireAdminPermission("admin.requests.review");
        await using var db=await factory.CreateDbContextAsync();
        var rows=await(from c in db.SupportConversations.AsNoTracking() join r in db.EmployerAccessRequests.AsNoTracking() on c.EmployerAccessRequestId equals r.Id
            join m in db.SupportMessages.AsNoTracking() on c.Id equals m.ConversationId
            join u in db.Users.AsNoTracking() on m.SenderId equals u.Id
            where r.WorkspaceId==workspaceId&&m.SenderId==c.UserId&&(r.RespondedUtc==null||m.CreatedUtc>r.RespondedUtc)
            orderby m.CreatedUtc select new{RequestId=r.Id,SessionId=c.Id,m.Id,m.SenderId,SenderName=u.FullName,m.Body,m.CreatedUtc,m.AttachmentName}).ToListAsync();
        return rows.GroupBy(x=>x.RequestId).ToDictionary(g=>g.Key,g=>new SupportRequestInformation(g.Key,g.Last().SessionId,g.Last().CreatedUtc,g.Select(m=>new SupportMessageView(m.Id,m.SenderId,m.SenderName,m.Body,m.CreatedUtc,m.AttachmentName)).ToList()));
    }

    public static async Task<(byte[] Content,string FileName)?> Transcript(AtsDbContext db,ClaimsPrincipal principal,Guid id)
    {
        if(!await CanReadSession(db,principal,id))return null;
        var c=await db.SupportConversations.AsNoTracking().SingleAsync(c=>c.Id==id);
        var messages=await(from m in db.SupportMessages.AsNoTracking() join u in db.Users.AsNoTracking() on m.SenderId equals u.Id where m.ConversationId==id orderby m.CreatedUtc select new{u.FullName,m.Body,m.CreatedUtc,m.AttachmentName}).ToListAsync();
        var text=new System.Text.StringBuilder($"Osynix Support — Chat transcript\nSession: {c.Id}\nIssue: {c.IssueType}\nSubject: {c.Subject}\nStarted: {c.CreatedUtc:yyyy-MM-dd HH:mm} UTC\nStatus: {c.Status}\nEnded: {(c.ClosedUtc is DateTime ended?ended.ToString("yyyy-MM-dd HH:mm")+" UTC":"—")}\n\n");
        foreach(var m in messages){text.AppendLine($"[{m.CreatedUtc:yyyy-MM-dd HH:mm} UTC] {m.FullName}");if(m.Body.Length>0)text.AppendLine(m.Body);if(m.AttachmentName.Length>0)text.AppendLine("Attachment: "+m.AttachmentName);text.AppendLine();}
        return(System.Text.Encoding.UTF8.GetBytes(text.ToString()),$"Osynix-chat-{c.CreatedUtc:yyyyMMdd}-{c.Id.ToString("N")[..8]}.txt");
    }

    static async Task<bool> CanReadSession(AtsDbContext db,ClaimsPrincipal principal,Guid id)
    {
        var uid=principal.FindFirstValue(ClaimTypes.NameIdentifier);if(principal.Identity?.IsAuthenticated!=true||string.IsNullOrEmpty(uid)||!await db.Users.AnyAsync(u=>u.Id==uid&&u.Status=="Active"))return false;
        var roles=await(from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==uid select r.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(c=>c.Value).Order()))return false;
        var admin=principal.IsInRole("Admin")||principal.IsInRole("SubAdmin")&&await db.UserClaims.AnyAsync(c=>c.UserId==uid&&c.ClaimType==Access.AdminPermissionClaim&&(c.ClaimValue=="admin.requests.review"||c.ClaimValue=="admin.workspaces.view"));
        return await db.SupportConversations.AnyAsync(c=>c.Id==id&&(admin||c.UserId==uid));
    }

    public async Task<Guid?> FindRequestConversation(Guid requestId)
    {
        await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var(user,adminSide)=await Current(db);
        var conversation=await db.SupportConversations.Where(c=>c.EmployerAccessRequestId==requestId&&(adminSide||c.UserId==user.Id)).OrderByDescending(c=>c.CreatedUtc).FirstOrDefaultAsync();
        if(conversation is not null)return conversation.Id;
        var request=await db.EmployerAccessRequests.SingleOrDefaultAsync(r=>r.Id==requestId);
        if(request is null||request.AdminResponse.Length==0)return null;
        var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==request.WorkspaceId);
        if(!adminSide&&workspace.OwnerId!=user.Id)return null;
        var audit=await db.Audit.AsNoTracking().Where(a=>a.Action=="Employer request responded"&&a.EntityId==request.Id.ToString()).OrderByDescending(a=>a.CreatedUtc).FirstOrDefaultAsync();
        var sender=audit is not null&&await db.Users.AnyAsync(u=>u.Id==audit.UserId)?audit.UserId:await db.Users.Where(u=>u.Status=="Active").Join(db.UserRoles, u=>u.Id, ur=>ur.UserId,(u,ur)=>new{u,ur}).Join(db.Roles,x=>x.ur.RoleId,r=>r.Id,(x,r)=>new{x.u,r.Name}).Where(x=>x.Name=="Admin").Select(x=>x.u.Id).FirstOrDefaultAsync();
        if(string.IsNullOrEmpty(sender))return null;
        conversation=new SupportConversation{UserId=workspace.OwnerId,WorkspaceId=workspace.Id,EmployerAccessRequestId=request.Id,Subject=request.SubscriptionPlanId is not null?"Subscription upgrade · "+request.Feature:request.Feature};
        db.SupportConversations.Add(conversation);db.SupportMessages.Add(new SupportMessage{ConversationId=conversation.Id,SenderId=sender,Body=$"Request update: {request.Status}\n\n{request.AdminResponse}",CreatedUtc=request.RespondedUtc??request.UpdatedUtc});conversation.LastMessageUtc=request.RespondedUtc??request.UpdatedUtc;
        try{await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateException){await tx.RollbackAsync();return await db.SupportConversations.AsNoTracking().Where(c=>c.EmployerAccessRequestId==requestId&&(adminSide||c.UserId==user.Id)).OrderByDescending(c=>c.CreatedUtc).Select(c=>(Guid?)c.Id).FirstOrDefaultAsync();}
        updates.Publish();return conversation.Id;
    }

    public async Task<SupportConversationDetail> Open(Guid id)
    {
        await using var db=await factory.CreateDbContextAsync();
        var (user,adminSide)=await Current(db);
        var conversation=await db.SupportConversations.SingleOrDefaultAsync(c=>c.Id==id&&(adminSide||c.UserId==user.Id))??throw new UnauthorizedAccessException("This support conversation is unavailable.");
        var contact=await db.Users.SingleAsync(u=>u.Id==conversation.UserId);
        var items=await(from m in db.SupportMessages.AsNoTracking() join sender in db.Users.AsNoTracking() on m.SenderId equals sender.Id where m.ConversationId==conversation.Id orderby m.CreatedUtc select new{m.Id,m.SenderId,SenderName=sender.FullName,m.Body,m.CreatedUtc,m.AttachmentName}).ToListAsync();
        var through=items.LastOrDefault()?.CreatedUtc??conversation.LastMessageUtc;var previousRead=adminSide?conversation.AdminReadUtc:conversation.UserReadUtc;
        if(adminSide)conversation.AdminReadUtc=through;else conversation.UserReadUtc=through;
        await db.SaveChangesAsync();
        if(previousRead is null||previousRead<through)updates.Publish();
        return new(conversation.Id,conversation.Subject,contact.Id,DisplayName(contact.FullName,contact.Email),contact.AccountType,conversation.Status,conversation.EmployerAccessRequestId,
            items.Select(m=>new SupportMessageView(m.Id,m.SenderId,m.SenderId==user.Id?"You":adminSide?DisplayName(m.SenderName,"Admin") :"Osynix Support",m.Body,m.CreatedUtc,m.AttachmentName)).ToList(),conversation.IssueType,conversation.CreatedUtc,conversation.ClosedUtc);
    }

    public async Task<Guid> StartGeneral(string issueType="General Support",string subject="Osynix Support")
    {
        subject=subject.Trim();
        if(!IssueTypes.Contains(issueType)||string.IsNullOrWhiteSpace(subject)||subject.Length>200)throw new InvalidOperationException("Choose an issue type and enter a subject of up to 200 characters.");
        await using var db=await factory.CreateDbContextAsync();
        var (user,adminSide)=await Current(db);
        if(adminSide)throw new UnauthorizedAccessException("Choose a user account to start a support conversation.");
        var existing=await db.SupportConversations.FirstOrDefaultAsync(c=>c.UserId==user.Id&&c.EmployerAccessRequestId==null&&c.Subject==subject&&c.IssueType==issueType&&c.Status=="Open");
        if(existing is not null)return existing.Id;
        var conversation=new SupportConversation{UserId=user.Id,Subject=subject.Trim(),IssueType=issueType,UserReadUtc=DateTime.UtcNow};
        db.SupportConversations.Add(conversation);
        try{await db.SaveChangesAsync();}
        catch(DbUpdateException){db.ChangeTracker.Clear();return await db.SupportConversations.Where(c=>c.UserId==user.Id&&c.EmployerAccessRequestId==null&&c.Subject==subject&&c.Status=="Open").Select(c=>c.Id).FirstAsync();}
        updates.Publish();return conversation.Id;
    }

    public async Task<Guid> StartForUser(string userId,string subject,string issueType="General Support")
    {
        subject=subject.Trim();if(subject.Length is <1 or >200)throw new InvalidOperationException("Enter a conversation subject of up to 200 characters.");if(string.Equals(subject,"Osynix Support",StringComparison.OrdinalIgnoreCase))subject="Support · Admin initiated";
        await using var db=await factory.CreateDbContextAsync();
        var (admin,adminSide)=await Current(db);if(!adminSide)throw new UnauthorizedAccessException();
        var target=await db.Users.SingleOrDefaultAsync(u=>u.Id==userId&&u.Status=="Active"&&u.AccountType!="Admin"&&u.AccountType!="SubAdmin"&&!db.UserRoles.Any(ur=>ur.UserId==u.Id&&db.Roles.Any(r=>r.Id==ur.RoleId&&(r.Name=="Admin"||r.Name=="SubAdmin"))))??throw new InvalidOperationException("Select an active user account.");
        if(!IssueTypes.Contains(issueType))throw new InvalidOperationException("Choose an issue type.");
        var conversation=new SupportConversation{UserId=target.Id,Subject=subject,IssueType=issueType,AdminReadUtc=DateTime.UtcNow};
        db.SupportConversations.Add(conversation);await db.SaveChangesAsync();updates.Publish();return conversation.Id;
    }

    public async Task<List<SupportUserOption>> Users()
    {
        await using var db=await factory.CreateDbContextAsync();var(_,adminSide)=await Current(db);if(!adminSide)throw new UnauthorizedAccessException();
        return await db.Users.AsNoTracking().Where(u=>u.Status=="Active"&&u.AccountType!="Admin"&&u.AccountType!="SubAdmin"&&!db.UserRoles.Any(ur=>ur.UserId==u.Id&&db.Roles.Any(r=>r.Id==ur.RoleId&&(r.Name=="Admin"||r.Name=="SubAdmin"))))
            .Select(u=>new SupportUserOption(u.Id,u.FullName,u.Email??"",u.AccountType)).OrderBy(u=>u.Name).ToListAsync();
    }

    public async Task Send(Guid conversationId,string body,string fileName="",byte[]? content=null)
    {
        body=body.Trim();if(body.Length>10000||(body.Length==0&&content is not{Length:>0}))throw new InvalidOperationException("Write a message of up to 10,000 characters or attach a file.");
        var type=ValidateAttachment(fileName,content);
        await using var db=await factory.CreateDbContextAsync();var(user,adminSide)=await Current(db);
        var conversation=await db.SupportConversations.SingleOrDefaultAsync(c=>c.Id==conversationId&&(adminSide||c.UserId==user.Id))??throw new UnauthorizedAccessException("This support conversation is unavailable.");
        if(conversation.Status=="Closed")throw new InvalidOperationException("This session has ended. Start a new support session.");
        db.SupportMessages.Add(new SupportMessage{ConversationId=conversation.Id,SenderId=user.Id,Body=body,AttachmentName=content is null?"":Path.GetFileName(fileName)[..Math.Min(Path.GetFileName(fileName).Length,200)],AttachmentType=type,AttachmentContent=content});
        conversation.LastMessageUtc=DateTime.UtcNow;
        if(adminSide)conversation.AdminReadUtc=conversation.LastMessageUtc;else conversation.UserReadUtc=conversation.LastMessageUtc;
        await db.SaveChangesAsync();updates.Publish();
    }

    public static async Task RecordRequestResponse(AtsDbContext db,EmployerAccessRequest request,EmployerWorkspace workspace,string adminId)
    {
        var conversation=await db.SupportConversations.SingleOrDefaultAsync(c=>c.EmployerAccessRequestId==request.Id&&c.Status=="Open");
        if(conversation is null){conversation=new SupportConversation{UserId=workspace.OwnerId,WorkspaceId=workspace.Id,EmployerAccessRequestId=request.Id,IssueType="Subscription & Payment",Subject=request.SubscriptionPlanId is not null?"Subscription upgrade · "+request.Feature:request.Feature};db.SupportConversations.Add(conversation);}
        var message=new SupportMessage{ConversationId=conversation.Id,SenderId=adminId,Body=$"Request update: {request.Status}\n\n{request.AdminResponse}"};
        db.SupportMessages.Add(message);conversation.LastMessageUtc=message.CreatedUtc;conversation.AdminReadUtc=message.CreatedUtc;
    }

    public static async Task<SupportMessage?> Download(AtsDbContext db,ClaimsPrincipal principal,Guid id)
    {
        var uid=principal.FindFirstValue(ClaimTypes.NameIdentifier);if(principal.Identity?.IsAuthenticated!=true||string.IsNullOrWhiteSpace(uid))return null;
        var actor=await db.Users.SingleOrDefaultAsync(u=>u.Id==uid&&u.Status=="Active");if(actor is null)return null;
        var roles=await(from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==uid select r.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(c=>c.Value).Order()))return null;
        var adminSide=principal.IsInRole("Admin");
        if(principal.IsInRole("SubAdmin")&&!adminSide){adminSide=await db.UserClaims.AnyAsync(c=>c.UserId==uid&&c.ClaimType==Access.AdminPermissionClaim&&c.ClaimValue=="admin.requests.review");}
        if(!adminSide&&!principal.IsInRole("Recruiter")&&!principal.IsInRole("Employer")&&!principal.IsInRole("Candidate"))return null;
        return await(from m in db.SupportMessages.AsNoTracking() join c in db.SupportConversations.AsNoTracking() on m.ConversationId equals c.Id
                     where m.Id==id&&(adminSide||c.UserId==uid) select m).SingleOrDefaultAsync();
    }

    static string DisplayName(string name,string? email)=>string.IsNullOrWhiteSpace(name)?email??"Account":name;
    static string ValidateAttachment(string name,byte[]? content)
    {
        if(content is not{Length:>0})return"";if(content.Length>5*1024*1024)throw new InvalidOperationException("Attachments must be 5 MB or smaller.");
        var ext=Path.GetExtension(name).ToLowerInvariant();return ext switch{
            ".pdf" when content.AsSpan().StartsWith("%PDF"u8)=>"application/pdf",
            ".png" when content.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})=>"image/png",
            ".jpg" or ".jpeg" when content.Length>3&&content[0]==255&&content[1]==216&&content[2]==255=>"image/jpeg",
            ".docx" when content.Length>4&&content[0]==80&&content[1]==75&&content[2]==3&&content[3]==4=>"application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _=>throw new InvalidOperationException("Attach a PDF, DOCX, PNG or JPEG file.")
        };
    }
}
