using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public record TeamMember(string Id,string Name,string Email,string Status,bool CanCreateClients,bool CanCreatePositions,bool CanExportReports,bool CanViewTalentPool,string ConcurrencyStamp,bool CanUseMasterPool=true,bool External=false,string Username="");
public record EmployerTeamOverview(EmployerWorkspace Workspace,List<TeamMember> Members,List<EmployerInvitation> Invitations);
public record InvitationView(string Company,string Email,string Name,string Status,DateTime ExpiresUtc);
public class EmployerTeamService(EmployerService employer,IDbContextFactory<AtsDbContext> factory,IDataProtectionProvider protection,IConfiguration config,IAccountEmail mail,RecruitmentUpdates? updates=null)
{
    readonly IDataProtector protector=protection.CreateProtector("Osynix.EmployerInvitation.v1");
    public bool CanEmail=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!uri.IsLoopback&&mail.Configured;
    string BaseUrl=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"?uri.GetLeftPart(UriPartial.Authority):"http://localhost:5080";
    static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<EmployerTeamOverview> Team()
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();var id=overview.Workspace.Id;
        if(overview.Workspace.Kind==AccountTypes.Freelance)throw new UnauthorizedAccessException("Upgrade To An Employer Account To Manage A Team.");
        var members=await (from m in db.WorkspaceMemberships.AsNoTracking() join u in db.Users on m.UserId equals u.Id where m.WorkspaceId==id orderby u.FullName select new TeamMember(u.Id,u.FullName,u.Email!,u.Status=="Active"?m.Status:"Inactive",m.CanCreateClients,m.CanCreatePositions,m.CanExportReports,m.CanViewTalentPool,m.Revision.ToString(),m.CanUseMasterPool,u.AccountType==AccountTypes.Freelance||u.AccountType==AccountTypes.Employer,u.UserName!)).ToListAsync();
        var invitations=await db.EmployerInvitations.AsNoTracking().Where(i=>i.WorkspaceId==id).OrderByDescending(i=>i.CreatedUtc).ToListAsync();return new(overview.Workspace,members,invitations);
    }
    public async Task<ForwardResult> Invite(string name,string email)
    {
        var owner=await employer.RequireAccount();var overview=await employer.Overview();if(overview.Workspace.Kind==AccountTypes.Freelance)throw new UnauthorizedAccessException("Upgrade To An Employer Account Before Inviting Team Members.");email=email.Trim();name=name.Trim();
        if(name.Length is <1 or >100||email.Length>320||!new EmailAddressAttribute().IsValid(email))throw new InvalidOperationException("Enter a name and a valid email address.");
        var normalized=email.ToUpperInvariant();await using var db=await factory.CreateDbContextAsync();
        if(await db.WorkspaceMemberships.AnyAsync(m=>m.WorkspaceId==overview.Workspace.Id&&db.Users.Any(u=>u.Id==m.UserId&&u.NormalizedEmail==normalized)))throw new InvalidOperationException("This Recruiter Is Already A Team Member. Manage Their Access Instead.");
        var invitation=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.WorkspaceId==overview.Workspace.Id&&i.NormalizedEmail==normalized);
        if(invitation?.Status=="Accepted")throw new InvalidOperationException("This invitation was accepted. Manage the team member instead.");
        if(invitation is null){invitation=new(){WorkspaceId=overview.Workspace.Id,NormalizedEmail=normalized};db.EmployerInvitations.Add(invitation);}
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));invitation.Email=email;invitation.FullName=name;invitation.TokenHash=Hash(token);invitation.ProtectedToken=protector.Protect(token);invitation.ExpiresUtc=DateTime.UtcNow.AddDays(7);invitation.Status="Pending";invitation.Delivery="Link Created";
        AtsService.Audit(db,owner.Id,"Recruiter invited",invitation.Id.ToString(),email);await db.SaveChangesAsync();
        updates?.Publish();return new(Link(token),"Invitation Created. Copy The Link And Share It With The Invited Recruiter. No Email Has Been Sent.");
    }
    string Link(string token)=>BaseUrl+"/Account/TeamInvitation?token="+Uri.EscapeDataString(token);
    public async Task<string> InvitationLink(Guid id)
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();var i=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.Id==id&&i.WorkspaceId==overview.Workspace.Id&&i.Status=="Pending"&&i.ExpiresUtc>DateTime.UtcNow)??throw new InvalidOperationException("This invitation is unavailable or expired.");return Link(protector.Unprotect(i.ProtectedToken));
    }
    public async Task EmailInvitation(Guid id)
    {
        if(!CanEmail)throw new InvalidOperationException("Email Not Sent. A Public HTTPS ATS Address And Working Email Settings Are Required.");
        var link=await InvitationLink(id);var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();var invitation=await db.EmployerInvitations.SingleAsync(i=>i.Id==id&&i.WorkspaceId==overview.Workspace.Id);
        try{await EmailTemplateService.Send(factory,mail,overview.Workspace.Id,"team-invitation",invitation.Email,"Your Osynix Team Invitation",$"You have been invited to join {overview.Workspace.CompanyName} as a Team Member.\n\nSign in or register with {invitation.Email}, verify your email, then accept this invitation:\n{link}\n\nThe invitation expires in 7 days. Internal Team Members Require An Available Seat. External Freelance Recruiters Do Not Use Internal Seats.",new(){["EmployerName"]=overview.Workspace.CompanyName,["InviteeEmail"]=invitation.Email,["InvitationLink"]=link});invitation.Delivery="Email Sent";}
        catch{invitation.Delivery="Email Failed";await db.SaveChangesAsync();throw new InvalidOperationException("We Could Not Confirm Invitation Email Delivery. Copy The Link Instead.");}
        AtsService.Audit(db,overview.Workspace.OwnerId,"Team invitation emailed",invitation.Id.ToString());await db.SaveChangesAsync();
    }
    public async Task Cancel(Guid id)
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();var invitation=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.Id==id&&i.WorkspaceId==overview.Workspace.Id&&i.Status=="Pending")??throw new UnauthorizedAccessException("Invitation unavailable.");invitation.Status="Cancelled";AtsService.Audit(db,overview.Workspace.OwnerId,"Team invitation cancelled",id.ToString());await db.SaveChangesAsync();updates?.Publish();
    }
    public async Task<InvitationView?> Read(string? token)
    {
        if(token is not {Length:64})return null;var hash=Hash(token);await using var db=await factory.CreateDbContextAsync();var invitation=await db.EmployerInvitations.AsNoTracking().SingleOrDefaultAsync(i=>i.TokenHash==hash);if(invitation is null)return null;
        var w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==invitation.WorkspaceId);
        if(!await OwnerActive(db,w.OwnerId))return null;
        return new(w.CompanyName,invitation.Email,invitation.FullName,invitation.Status=="Pending"&&invitation.ExpiresUtc<DateTime.UtcNow?"Expired":invitation.Status,invitation.ExpiresUtc);
    }
    static Task<bool> OwnerActive(AtsDbContext db,string id)=>WorkspaceContext.OwnerActive(db,id);
    public async Task<Guid> Accept(string? token,ClaimsPrincipal principal)
    {
        if(token is not {Length:64}||principal.Identity?.IsAuthenticated!=true)throw new UnauthorizedAccessException("Sign In With The Invited Account.");
        var userId=principal.FindFirstValue(ClaimTypes.NameIdentifier);await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var user=await db.Users.FindAsync(userId);
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==userId select r.Name).ToListAsync();
        if(user is null||!user.EmailConfirmed||user.Status!="Active"||roles.Count!=1||roles[0] is not("Recruiter" or "Employer")||!principal.IsInRole(roles[0]!))throw new UnauthorizedAccessException("A Verified Active Operational Account Is Required.");
        var hash=Hash(token);var invitation=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.TokenHash==hash)??throw new InvalidOperationException("Invitation Unavailable.");
        if(!string.Equals(user.Email,invitation.Email,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Sign In With The Email Address On This Invitation.");
        if(invitation.Status!="Pending"||invitation.ExpiresUtc<DateTime.UtcNow)throw new InvalidOperationException("This Invitation Has Expired Or Is No Longer Pending.");
        if(await db.WorkspaceMemberships.AnyAsync(m=>m.UserId==user.Id&&m.WorkspaceId==invitation.WorkspaceId))throw new InvalidOperationException("You Already Have A Membership In This Workspace. Ask Its Owner To Manage Your Access.");
        if(user.AccountType==AccountTypes.Legacy&&(user.EmployerWorkspaceId is not null||await db.Positions.AnyAsync(p=>p.AssignedRecruiter==user.UserName)))throw new InvalidOperationException("Legacy Account Requires A Workspace Migration Before Joining Another Team.");
        var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==invitation.WorkspaceId);if(workspace.Kind==AccountTypes.Freelance||workspace.OwnerId==user.Id||!await OwnerActive(db,workspace.OwnerId))throw new UnauthorizedAccessException("This Employer Workspace Cannot Accept This Membership.");
        if(user.AccountType is not(AccountTypes.Freelance or AccountTypes.Employer))await CheckCapacity(db,workspace,user.Id);
        db.WorkspaceMemberships.Add(new(){WorkspaceId=workspace.Id,UserId=user.Id});
        if(user.AccountType==AccountTypes.Legacy){user.EmployerWorkspaceId=workspace.Id;user.CanCreateClients=false;user.CanCreatePositions=false;user.CanExportReports=false;user.CanViewTalentPool=true;user.TalentPoolPaidUntilUtc=null;}
        invitation.Status="Accepted";invitation.AcceptedBy=user.Id;AtsService.Audit(db,user.Id,"Team invitation accepted",invitation.Id.ToString(),workspace.Id.ToString());await db.SaveChangesAsync();await tx.CommitAsync();updates?.Publish();return workspace.Id;
    }
    static async Task CheckCapacity(AtsDbContext db,EmployerWorkspace w,string userId)
    {
        var used=await db.WorkspaceMemberships.CountAsync(m=>m.WorkspaceId==w.Id&&m.Status=="Active"&&m.UserId!=userId&&db.Users.Any(u=>u.Id==m.UserId&&u.Status=="Active"&&u.AccountType!=AccountTypes.Freelance&&u.AccountType!=AccountTypes.Employer));
        if(used>=w.FreeRecruiterSeats+w.AdditionalRecruiterSeats)throw new InvalidOperationException("No Recruiter Seat Is Available. Ask Your Employer To Request More Seats From Admin.");
    }
    public async Task Update(TeamMember input)
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        var member=await db.WorkspaceMemberships.SingleOrDefaultAsync(m=>m.UserId==input.Id&&m.WorkspaceId==overview.Workspace.Id)??throw new UnauthorizedAccessException("This Member Is Not In Your Team.");
        var user=await db.Users.SingleAsync(u=>u.Id==member.UserId);
        if(!Guid.TryParse(input.ConcurrencyStamp,out var expectedRevision)||member.Revision!=expectedRevision)throw new DbUpdateConcurrencyException("Team Access Changed. Reload Before Saving.");
        if(input.Status is not("Active" or "Inactive"))throw new InvalidOperationException("Choose Active Or Inactive.");
        if(input.Status=="Active"&&member.Status!="Active"&&user.AccountType is not(AccountTypes.Freelance or AccountTypes.Employer))await CheckCapacity(db,await db.EmployerWorkspaces.SingleAsync(w=>w.Id==overview.Workspace.Id),user.Id);
        member.Status=input.Status;member.CanCreateClients=input.CanCreateClients;member.CanCreatePositions=input.CanCreatePositions;member.CanExportReports=input.CanExportReports;member.CanViewTalentPool=input.CanViewTalentPool;member.CanUseMasterPool=input.CanUseMasterPool;
        if(user.AccountType==AccountTypes.Legacy){user.Status=input.Status;user.CanCreateClients=input.CanCreateClients;user.CanCreatePositions=input.CanCreatePositions;user.CanExportReports=input.CanExportReports;user.CanViewTalentPool=input.CanViewTalentPool;user.SecurityStamp=Guid.NewGuid().ToString();user.ConcurrencyStamp=Guid.NewGuid().ToString();}
        AtsService.Audit(db,overview.Workspace.OwnerId,"Team member access updated",user.Id,$"Workspace={member.WorkspaceId}; {member.Status}; clients={member.CanCreateClients}; positions={member.CanCreatePositions}; exports={member.CanExportReports}; pool={member.CanViewTalentPool}; master={member.CanUseMasterPool}");await db.SaveChangesAsync();await tx.CommitAsync();updates?.Publish();
    }

    public async Task<List<Position>> AssignablePositions(){var team=await Team();await using var db=await factory.CreateDbContextAsync();return await db.Positions.AsNoTracking().Where(p=>p.WorkspaceId==team.Workspace.Id&&(p.Status=="Active"||p.Status=="On Hold")).OrderBy(p=>p.Reference).ToListAsync();}
    public async Task<List<Guid>> Assignments(string userId){var team=await Team();if(!team.Members.Any(m=>m.Id==userId))throw new UnauthorizedAccessException();await using var db=await factory.CreateDbContextAsync();return await db.Positions.Where(p=>p.WorkspaceId==team.Workspace.Id&&(p.AssignedRecruiter==db.Users.Where(u=>u.Id==userId).Select(u=>u.UserName).FirstOrDefault()||db.PositionRecruiterAssignments.Any(a=>a.PositionId==p.Id&&a.UserId==userId))).Select(p=>p.Id).ToListAsync();}
    public async Task AssignPositions(string userId,IEnumerable<Guid> selected){var team=await Team();var member=team.Members.SingleOrDefault(m=>m.Id==userId)??throw new UnauthorizedAccessException();var ids=selected.Distinct().ToList();await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var membership=await db.WorkspaceMemberships.SingleAsync(m=>m.WorkspaceId==team.Workspace.Id&&m.UserId==userId);if(membership.Status!="Active"||!await db.Users.AnyAsync(u=>u.Id==userId&&u.Status=="Active"))throw new InvalidOperationException("Activate This Membership Before Assigning Positions.");var positions=await db.Positions.Where(p=>p.WorkspaceId==team.Workspace.Id).ToListAsync();if(ids.Any(id=>!positions.Any(p=>p.Id==id&&(p.Status=="Active"||p.Status=="On Hold"))))throw new InvalidOperationException("Select Open Positions From Your Own Workspace.");var username=(await db.Users.SingleAsync(u=>u.Id==userId)).UserName;var old=await db.PositionRecruiterAssignments.Where(a=>a.UserId==userId&&positions.Select(p=>p.Id).Contains(a.PositionId)).ToListAsync();db.PositionRecruiterAssignments.RemoveRange(old.Where(a=>!ids.Contains(a.PositionId)&&positions.Any(p=>p.Id==a.PositionId&&(p.Status=="Active"||p.Status=="On Hold"))));foreach(var p in positions.Where(p=>p.Status=="Active"||p.Status=="On Hold")){if(p.AssignedRecruiter==username&&!ids.Contains(p.Id))p.AssignedRecruiter="";if(ids.Contains(p.Id)&&p.AssignedRecruiter!=username&&!old.Any(a=>a.PositionId==p.Id))db.PositionRecruiterAssignments.Add(new(){UserId=userId,PositionId=p.Id});}AtsService.Audit(db,team.Workspace.OwnerId,"Recruiter position assignments updated",userId,string.Join(',',ids));await db.SaveChangesAsync();await tx.CommitAsync();updates?.Publish();}
}
