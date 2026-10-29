using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record PortalMessageView(Guid Id,Guid PositionId,string Position,string SenderName,string Body,bool FromClient,DateTime SentUtc,bool Unread);
public record PortalConversation(Guid Id,string Name,string Context,string Preview,DateTime UpdatedUtc,int Unread);
public record PortalThread(Guid Id,string Name,List<HiringPosition> Positions,List<PortalMessageView> Messages);
public record PortalMessageInput(Guid PositionId,string Body);

public partial class HiringReviewService
{
    public async Task<bool> PortalAllowed(Guid? workspaceId)
    {
        if(workspaceId is not Guid id)return true;
        await using var db=await factory.CreateDbContextAsync();
        await SubscriptionLifecycle.ApplyDue(db,id);
        return await SubscriptionRules.PaidPortal(db,await db.EmployerWorkspaces.SingleAsync(w=>w.Id==id));
    }

    HiringReviewGrant NewPortalGrant(Guid clientId,Guid? contactId,Guid? positionId,string name)
    {
        var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return new(){ClientId=clientId,ContactId=contactId,PositionId=positionId,
            EmailKey=positionId is Guid pid?"position:"+pid.ToString("N"):"portal-contact:"+contactId!.Value.ToString("N"),
            Name=name,TokenHash=Hash(token),ProtectedToken=tokenProtector.Protect(token),ExpiresUtc=DateTime.SpecifyKind(DateTime.MaxValue,DateTimeKind.Utc)};
    }
    string PortalLink(HiringReviewGrant grant)=>BaseUrl+"/review/access?token="+Uri.EscapeDataString(tokenProtector.Unprotect(grant.ProtectedToken))+(grant.PositionId is Guid id?"&position="+id:"");

    public async Task ConfigureClient(AtsDbContext db,Client client,Dictionary<Guid,string>? oldEmails=null)
    {
        var grants=await db.HiringReviewGrants.Where(g=>g.ClientId==client.Id&&g.ContactId!=null).ToListAsync();
        var paid=client.WorkspaceId is not Guid wid||await SubscriptionRules.PaidPortal(db,await db.EmployerWorkspaces.SingleAsync(w=>w.Id==wid));
        foreach(var grant in grants.Where(g=>!client.Contacts.Any(c=>c.Id==g.ContactId&&c.PortalEnabled)||client.Status!="Active"))grant.Revoked=true;
        foreach(var contact in client.Contacts.Where(c=>c.PortalEnabled&&client.Status=="Active"))
        {
            if(!paid)continue;
            if(string.IsNullOrWhiteSpace(contact.Name))throw new InvalidOperationException("Enter a name for each contact receiving portal access.");
            var grant=grants.SingleOrDefault(g=>g.ContactId==contact.Id);
            if(grant is null){grant=NewPortalGrant(client.Id,contact.Id,null,client.Name);db.HiringReviewGrants.Add(grant);}
            var enabling=grant.Revoked;
            if(enabling||oldEmails?.TryGetValue(contact.Id,out var oldEmail)==true&&oldEmail!=contact.Email)RotatePortal(grant);
            grant.Name=client.Name;grant.Revoked=false;
            QueueInvitation(db,grant,contact.Email,contact.Name,client.Name,enabling);
        }
        if(client.Contacts.Any(c=>oldEmails?.TryGetValue(c.Id,out var address)==true&&address!=c.Email))
            foreach(var legacy in await db.HiringReviewGrants.Where(g=>g.ClientId==client.Id&&g.ContactId==null&&g.PositionId==null).ToListAsync())legacy.Revoked=true;
        if(!client.Contacts.Any(c=>c.PortalEnabled))
            foreach(var legacy in await db.HiringReviewGrants.Where(g=>g.ClientId==client.Id&&g.ContactId==null&&g.PositionId==null).ToListAsync())legacy.Revoked=true;
    }

    public async Task ConfigurePosition(AtsDbContext db,Position position,string previousEmail="")
    {
        var grant=await db.HiringReviewGrants.SingleOrDefaultAsync(g=>g.PositionId==position.Id);
        var paid=position.WorkspaceId is not Guid wid||await SubscriptionRules.PaidPortal(db,await db.EmployerWorkspaces.SingleAsync(w=>w.Id==wid));
        if(!position.HiringManagerPortalEnabled){if(grant is not null)grant.Revoked=true;return;}
        if(!paid){if(grant is not null&&previousEmail!=position.HiringManagerEmail)grant.Revoked=true;return;}
        if(string.IsNullOrWhiteSpace(position.HiringContact))throw new InvalidOperationException("Enter a hiring manager name before enabling portal access.");
        if(grant is null){grant=NewPortalGrant(position.ClientId!.Value,null,position.Id,position.Client);db.HiringReviewGrants.Add(grant);}
        var enabling=grant.Revoked;
        if(enabling||previousEmail!=position.HiringManagerEmail&&grant.CreatedUtc<DateTime.UtcNow.AddSeconds(-1))RotatePortal(grant);
        grant.ClientId=position.ClientId;grant.Name=position.Client;grant.Revoked=false;
        if(previousEmail.Length>0&&previousEmail!=position.HiringManagerEmail)
            foreach(var legacy in await db.HiringReviewGrants.Where(g=>g.ClientId==position.ClientId&&g.ContactId==null&&g.PositionId==null).ToListAsync())legacy.Revoked=true;
        QueueInvitation(db,grant,position.HiringManagerEmail,position.HiringContact,position.Title,enabling);
    }
    void RotatePortal(HiringReviewGrant grant){var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));grant.TokenHash=Hash(token);grant.ProtectedToken=tokenProtector.Protect(token);}

    void QueueInvitation(AtsDbContext db,HiringReviewGrant grant,string address,string name,string context,bool renewed)
    {
        if(string.IsNullOrWhiteSpace(address))return;
        var key=$"portal-invite:{grant.Id}:{grant.TokenHash}:{address.Trim().ToLowerInvariant()}";
        if(db.PortalEmailDeliveries.Local.Any(e=>e.Key==key)||db.PortalEmailDeliveries.Any(e=>e.Key==key))return;
        db.PortalEmailDeliveries.Add(new(){Key=key,Email=address.Trim(),GrantId=grant.Id,RequiresPublicAddress=true,Subject=$"Your client / hiring manager portal · {context}",
            Body=$"Hello {name},\n\nYou have been given operational review access for {context}. Review shared positions and candidate profiles, provide feedback, coordinate interview availability and message your recruiter here:\n\n[PortalAccessLink]\n\nThis invitation grants only client / hiring manager portal access. Keep the link private."});
    }

    public async Task<ClientReviewLink> PortalAccess(Guid clientId,Guid? positionId=null,Guid? contactId=null)
    {
        await access.RequireClient(clientId);if(positionId is Guid pid)await access.RequirePosition(pid);
        await using var db=await factory.CreateDbContextAsync();
        var client=await db.Clients.SingleAsync(c=>c.Id==clientId);await SubscriptionRules.RequireHiringReviews(db,client.WorkspaceId);
        if(positionId is Guid position&&!await db.Positions.AnyAsync(p=>p.Id==position&&p.ClientId==clientId))throw new UnauthorizedAccessException();
        var grant=await db.HiringReviewGrants.AsNoTracking().Where(g=>g.ClientId==clientId&&(positionId!=null?g.PositionId==positionId:contactId!=null?g.ContactId==contactId:g.PositionId==null&&g.ContactId==null)).FirstOrDefaultAsync();
        if(grant is null&&(positionId is Guid existingPosition&&await db.Positions.AnyAsync(p=>p.Id==existingPosition&&p.HiringManagerPortalEnabled)||contactId is Guid existingContact&&await db.ClientContacts.AnyAsync(c=>c.Id==existingContact&&c.ClientId==clientId&&c.PortalEnabled)))
            grant=await db.HiringReviewGrants.AsNoTracking().FirstOrDefaultAsync(g=>g.ClientId==clientId&&g.PositionId==null&&g.ContactId==null&&!g.Revoked);
        if(grant is null)return new(null,"Save to enable portal access",null,0);
        var enabled=await PortalGrantEnabled(db,grant);
        var status=grant.Revoked||!enabled?"Access disabled":grant.ExpiresUtc<=DateTime.UtcNow?"Expired":"Active";
        return new(status=="Active"?PortalLink(grant)+(positionId is Guid contextPosition&&grant.PositionId==null?"&position="+contextPosition:""):null,status,grant.ExpiresUtc,await Visible(db,grant).CountAsync());
    }

    static async Task<bool> PortalGrantEnabled(AtsDbContext db,HiringReviewGrant grant)
    {
        if(grant.ClientId is not Guid cid||!await db.Clients.AnyAsync(c=>c.Id==cid&&c.Status=="Active"))return false;
        if(grant.PositionId is Guid pid)return await db.Positions.AnyAsync(p=>p.Id==pid&&p.ClientId==cid&&p.HiringManagerPortalEnabled);
        if(grant.ContactId is Guid contact)return await db.ClientContacts.AnyAsync(c=>c.Id==contact&&c.ClientId==cid&&c.PortalEnabled);
        var companyPositions=db.Positions.Where(p=>p.ClientId==cid&&!p.IsAgencyPosition&&db.EmployerWorkspaces.Any(w=>w.Id==p.WorkspaceId&&w.Kind==AccountTypes.Employer));
        if(await companyPositions.AnyAsync())return await companyPositions.AnyAsync(p=>p.HiringManagerPortalEnabled)||await db.Positions.AnyAsync(p=>p.ClientId==cid&&p.IsAgencyPosition&&db.ClientContacts.Any(c=>c.ClientId==cid&&c.PortalEnabled));
        return true; // Legacy client-wide links retain their original client boundary.
    }

    async Task<ForwardResult> NotifyOnly(AtsDbContext db,List<Assessment> apps,string address,string uid)
    {
        var position=apps[0].Position!;
        foreach(var app in apps)
        {
            if(position.WorkspaceId is Guid workspaceId){var submission=await db.EmployerProfileSubmissions.SingleOrDefaultAsync(s=>s.AssessmentId==app.Id&&s.WorkspaceId==workspaceId);if(submission is null)db.EmployerProfileSubmissions.Add(new(){WorkspaceId=workspaceId,AssessmentId=app.Id,SubmittedBy=uid});else{submission.Status="Awaiting Employer Review";submission.SubmittedBy=uid;submission.SubmittedUtc=DateTime.UtcNow;submission.UpdatedUtc=DateTime.UtcNow;}}
            app.Stage="Awaiting Employer Review";app.ClientStatus="Submitted";
            AtsService.Audit(db,uid,"Profiles submitted for review",app.Id.ToString(),"Notification only; no report or portal access shared",app.CandidateId,app.Id);
        }
        if(new EmailAddressAttribute().IsValid(address))
        {
            var selectedIds=apps.Select(a=>a.Id).ToList();
            var pending=apps.Count+await db.Assessments.CountAsync(a=>a.PositionId==position.Id&&!selectedIds.Contains(a.Id)&&a.State=="Saved"&&(a.Stage=="Awaiting Employer Review"||a.Stage=="Awaiting Client Feedback"));
            db.PortalEmailDeliveries.Add(new(){Key="activity:"+Guid.NewGuid().ToString("N"),Email=address,
                Subject=$"Profiles pending review · {position.Title}",Body=$"Hello {position.HiringContact},\n\n{Math.Max(pending,apps.Count)} profile(s) are pending review for {position.Title} (#{position.Reference}). Please contact your recruiter for the next step.\n\nThis is an activity notification. No candidate profile, report, assessment or portal access is included."});
        }
        await db.SaveChangesAsync();updates.Publish();
        return new("",new EmailAddressAttribute().IsValid(address)?"Profiles submitted. A notification email is queued; no report, assessment or portal access is shared.":"Profiles submitted for review. Add a hiring manager email to receive activity notifications.");
    }

    public async Task<PortalThread> PortalMessages(string? cookie,bool markRead=false)
    {
        await using var db=await factory.CreateDbContextAsync();var grant=await Grant(db,cookie);var positions=(await Read(cookie)).Positions;var ids=positions.Select(p=>p.Id).ToList();
        var messages=await db.ClientPortalMessages.Where(m=>m.GrantId==grant.Id&&ids.Contains(m.PositionId)).OrderBy(m=>m.CreatedUtc).Take(500).ToListAsync();
        if(markRead)foreach(var m in messages.Where(m=>!m.FromClient&&m.ReadUtc==null))m.ReadUtc=DateTime.UtcNow;
        await db.SaveChangesAsync();return new(grant.Id,grant.Name,positions,messages.Select(m=>MessageView(m,positions)).ToList());
    }
    static PortalMessageView MessageView(ClientPortalMessage m,List<HiringPosition> positions)=>new(m.Id,m.PositionId,positions.FirstOrDefault(p=>p.Id==m.PositionId)?.Title??"Position",m.SenderName,m.Body,m.FromClient,m.CreatedUtc,!m.FromClient&&m.ReadUtc==null);
    public async Task PortalSend(string? cookie,PortalMessageInput input)
    {
        if(string.IsNullOrWhiteSpace(input.Body)||input.Body.Length>10000)throw new InvalidOperationException("Write a message of up to 10,000 characters.");
        await using var db=await factory.CreateDbContextAsync();var grant=await Grant(db,cookie);
        if(!(await Read(cookie)).Positions.Any(p=>p.Id==input.PositionId))throw new UnauthorizedAccessException("This position is not shared with you.");
        db.ClientPortalMessages.Add(new(){GrantId=grant.Id,PositionId=input.PositionId,FromClient=true,SenderName=grant.Name+" · Client team",Body=input.Body.Trim()});
        AtsService.Audit(db,"HiringManager","Client portal message",grant.Id.ToString(),"Client message received for position "+input.PositionId);
        await db.SaveChangesAsync();updates.Publish();
    }
    public async Task<List<PortalConversation>> PortalInbox()
    {
        var positions=await new AtsService(factory,access).Positions();var ids=positions.Select(p=>p.Id).ToList();await using var db=await factory.CreateDbContextAsync();
        var messages=await db.ClientPortalMessages.AsNoTracking().Where(m=>ids.Contains(m.PositionId)).OrderByDescending(m=>m.CreatedUtc).ToListAsync();
        var grantIds=messages.Select(m=>m.GrantId).Distinct().ToList();var grants=await db.HiringReviewGrants.Where(g=>grantIds.Contains(g.Id)).ToDictionaryAsync(g=>g.Id);
        return messages.GroupBy(m=>m.GrantId).Select(g=>new PortalConversation(g.Key,grants[g.Key].Name,"Client / Hiring Manager portal",g.First().Body,g.First().CreatedUtc,g.Count(m=>m.FromClient&&m.ReadUtc==null))).ToList();
    }
    public async Task<PortalThread> PortalOpen(Guid grantId)
    {
        var positions=await new AtsService(factory,access).Positions();var ids=positions.Select(p=>p.Id).ToList();await using var db=await factory.CreateDbContextAsync();
        var grant=await db.HiringReviewGrants.SingleOrDefaultAsync(g=>g.Id==grantId)??throw new UnauthorizedAccessException();
        var messages=await db.ClientPortalMessages.Where(m=>m.GrantId==grantId&&ids.Contains(m.PositionId)).OrderBy(m=>m.CreatedUtc).Take(500).ToListAsync();
        if(messages.Count==0)throw new UnauthorizedAccessException("This client conversation is not assigned to you.");
        foreach(var m in messages.Where(m=>m.FromClient&&m.ReadUtc==null))m.ReadUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        return new(grant.Id,grant.Name,positions.Where(p=>messages.Any(m=>m.PositionId==p.Id)).Select(p=>new HiringPosition(p.Id,p.Reference,p.Title)).ToList(),messages.Select(m=>MessageView(m,positions.Select(p=>new HiringPosition(p.Id,p.Reference,p.Title)).ToList())).ToList());
    }
    public async Task PortalReply(Guid grantId,Guid positionId,string body)
    {
        if(string.IsNullOrWhiteSpace(body)||body.Length>10000)throw new InvalidOperationException("Write a message of up to 10,000 characters.");
        await access.RequirePosition(positionId);var thread=await PortalOpen(grantId);
        if(!thread.Positions.Any(p=>p.Id==positionId))throw new UnauthorizedAccessException();
        await using var db=await factory.CreateDbContextAsync();var grant=await db.HiringReviewGrants.SingleAsync(g=>g.Id==grantId);
        if(grant.Revoked||!await PortalGrantEnabled(db,grant))throw new InvalidOperationException("Client portal access has been disabled.");
        var actor=await access.Require();db.ClientPortalMessages.Add(new(){GrantId=grantId,PositionId=positionId,SenderName=actor.Identity!.Name!,Body=ReportService.ClientSafeText(body.Trim())});
        AtsService.Audit(db,await access.UserId(),"Client portal reply",grantId.ToString(),"Client-safe reply sent");await db.SaveChangesAsync();updates.Publish();
    }
}
