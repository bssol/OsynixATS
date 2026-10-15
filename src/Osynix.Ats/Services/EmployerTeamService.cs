using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public record TeamMember(string Id,string Name,string Email,string Status,bool CanCreateClients,bool CanCreatePositions,bool CanExportReports,bool CanViewTalentPool,string ConcurrencyStamp);
public record EmployerTeamOverview(EmployerWorkspace Workspace,List<TeamMember> Members,List<EmployerInvitation> Invitations);
public record InvitationView(string Company,string Email,string Name,string Status,DateTime ExpiresUtc);
public class EmployerTeamService(EmployerService employer,IDbContextFactory<AtsDbContext> factory,IDataProtectionProvider protection,IConfiguration config,IAccountEmail mail)
{
    readonly IDataProtector protector=protection.CreateProtector("Osynix.EmployerInvitation.v1");
    public bool CanEmail=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!uri.IsLoopback&&mail.Configured;
    string BaseUrl=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"?uri.GetLeftPart(UriPartial.Authority):"http://localhost:5080";
    static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<EmployerTeamOverview> Team()
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();var id=overview.Workspace.Id;
        var members=await db.Users.AsNoTracking().Where(u=>u.EmployerWorkspaceId==id).OrderBy(u=>u.FullName).Select(u=>new TeamMember(u.Id,u.FullName,u.Email!,u.Status,u.CanCreateClients,u.CanCreatePositions,u.CanExportReports,u.CanViewTalentPool,u.ConcurrencyStamp!)).ToListAsync();
        var invitations=await db.EmployerInvitations.AsNoTracking().Where(i=>i.WorkspaceId==id).OrderByDescending(i=>i.CreatedUtc).ToListAsync();return new(overview.Workspace,members,invitations);
    }
    public async Task<ForwardResult> Invite(string name,string email)
    {
        var owner=await employer.RequireAccount();var overview=await employer.Overview();email=email.Trim();name=name.Trim();
        if(name.Length is <1 or >100||email.Length>320||!new EmailAddressAttribute().IsValid(email))throw new InvalidOperationException("Enter a name and a valid email address.");
        var normalized=email.ToUpperInvariant();await using var db=await factory.CreateDbContextAsync();
        if(await db.Users.AnyAsync(u=>u.EmployerWorkspaceId==overview.Workspace.Id&&u.NormalizedEmail==normalized))throw new InvalidOperationException("This Recruiter Is Already A Team Member. Manage Their Access Instead.");
        var invitation=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.WorkspaceId==overview.Workspace.Id&&i.NormalizedEmail==normalized);
        if(invitation?.Status=="Accepted")throw new InvalidOperationException("This invitation was accepted. Manage the team member instead.");
        if(invitation is null){invitation=new(){WorkspaceId=overview.Workspace.Id,NormalizedEmail=normalized};db.EmployerInvitations.Add(invitation);}
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));invitation.Email=email;invitation.FullName=name;invitation.TokenHash=Hash(token);invitation.ProtectedToken=protector.Protect(token);invitation.ExpiresUtc=DateTime.UtcNow.AddDays(7);invitation.Status="Pending";invitation.Delivery="Link Created";
        AtsService.Audit(db,owner.Id,"Recruiter invited",invitation.Id.ToString(),email);await db.SaveChangesAsync();
        return new(Link(token),"Invitation Created. Copy The Link And Share It With The Invited Recruiter. No Email Has Been Sent.");
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
        try{await mail.Send(invitation.Email,"Your Osynix Recruiter Team Invitation",$"You have been invited to join {overview.Workspace.CompanyName} as a Recruiter.\n\nSign in or register with {invitation.Email}, verify your email, then accept this invitation:\n{link}\n\nThe invitation expires in 7 days. Acceptance requires an available team seat.");invitation.Delivery="Email Sent";}
        catch{invitation.Delivery="Email Failed";await db.SaveChangesAsync();throw new InvalidOperationException("We Could Not Confirm Invitation Email Delivery. Copy The Link Instead.");}
        AtsService.Audit(db,overview.Workspace.OwnerId,"Team invitation emailed",invitation.Id.ToString());await db.SaveChangesAsync();
    }
    public async Task Cancel(Guid id)
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();var invitation=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.Id==id&&i.WorkspaceId==overview.Workspace.Id&&i.Status=="Pending")??throw new UnauthorizedAccessException("Invitation unavailable.");invitation.Status="Cancelled";AtsService.Audit(db,overview.Workspace.OwnerId,"Team invitation cancelled",id.ToString());await db.SaveChangesAsync();
    }
    public async Task<InvitationView?> Read(string? token)
    {
        if(token is not {Length:64})return null;var hash=Hash(token);await using var db=await factory.CreateDbContextAsync();var invitation=await db.EmployerInvitations.AsNoTracking().SingleOrDefaultAsync(i=>i.TokenHash==hash);if(invitation is null)return null;
        var w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==invitation.WorkspaceId);
        if(!await OwnerActive(db,w.OwnerId))return null;
        return new(w.CompanyName,invitation.Email,invitation.FullName,invitation.Status=="Pending"&&invitation.ExpiresUtc<DateTime.UtcNow?"Expired":invitation.Status,invitation.ExpiresUtc);
    }
    static Task<bool> OwnerActive(AtsDbContext db,string id)=>db.Users.AnyAsync(u=>u.Id==id&&u.Status=="Active"&&u.EmailConfirmed&&db.UserRoles.Any(ur=>ur.UserId==id&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Employer")));
    public async Task Accept(string? token,ClaimsPrincipal principal)
    {
        if(token is not {Length:64}||principal.Identity?.IsAuthenticated!=true)throw new UnauthorizedAccessException("Sign in with the invited Recruiter account.");
        var userId=principal.FindFirstValue(ClaimTypes.NameIdentifier);await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var user=await db.Users.FindAsync(userId);
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==userId select r.Name).ToListAsync();
        if(user is null||!user.EmailConfirmed||user.Status!="Active"||roles.Count!=1||roles[0]!="Recruiter"||!principal.IsInRole("Recruiter"))throw new UnauthorizedAccessException("A Verified Active Recruiter Account Is Required.");
        var hash=Hash(token);var invitation=await db.EmployerInvitations.SingleOrDefaultAsync(i=>i.TokenHash==hash)??throw new InvalidOperationException("Invitation unavailable.");
        if(!string.Equals(user.Email,invitation.Email,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Sign In With The Email Address On This Invitation.");
        if(invitation.Status!="Pending"||invitation.ExpiresUtc<DateTime.UtcNow)throw new InvalidOperationException("This Invitation Has Expired Or Is No Longer Pending.");
        if(user.EmployerWorkspaceId is not null)throw new InvalidOperationException("Your Account Already Belongs To An Employer Team.");
        if(await db.Positions.AnyAsync(p=>p.AssignedRecruiter==user.UserName)||await db.Audit.AnyAsync(a=>a.UserId==user.Id&&a.Action=="Client saved"))throw new InvalidOperationException("Your Account Has Existing Recruitment Work. Ask Admin To Review It Before Joining A Team.");
        var workspace=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==invitation.WorkspaceId);if(!await OwnerActive(db,workspace.OwnerId))throw new UnauthorizedAccessException("This Employer Workspace Is Inactive.");
        await CheckCapacity(db,workspace,user.Id);
        user.EmployerWorkspaceId=workspace.Id;user.CanCreateClients=false;user.CanCreatePositions=false;user.CanExportReports=false;user.CanViewTalentPool=true;user.TalentPoolPaidUntilUtc=null;user.ConcurrencyStamp=Guid.NewGuid().ToString();user.SecurityStamp=Guid.NewGuid().ToString();
        invitation.Status="Accepted";invitation.AcceptedBy=user.Id;AtsService.Audit(db,user.Id,"Team invitation accepted",invitation.Id.ToString(),workspace.Id.ToString());await db.SaveChangesAsync();await tx.CommitAsync();
    }
    static async Task CheckCapacity(AtsDbContext db,EmployerWorkspace w,string userId)
    {
        var used=await db.Users.CountAsync(u=>u.EmployerWorkspaceId==w.Id&&u.Status=="Active"&&u.Id!=userId);
        if(used>=w.FreeRecruiterSeats+w.AdditionalRecruiterSeats)throw new InvalidOperationException("No Recruiter Seat Is Available. Ask Your Employer To Request More Seats From Admin.");
    }
    public async Task Update(TeamMember input)
    {
        var overview=await employer.Overview();await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        var user=await db.Users.SingleOrDefaultAsync(u=>u.Id==input.Id&&u.EmployerWorkspaceId==overview.Workspace.Id)??throw new UnauthorizedAccessException("This member is not in your team.");
        if(!await db.UserRoles.AnyAsync(ur=>ur.UserId==user.Id&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Recruiter"))||await db.UserRoles.CountAsync(ur=>ur.UserId==user.Id)!=1)throw new UnauthorizedAccessException("Only Recruiter Members Can Be Managed Here.");
        if(user.ConcurrencyStamp!=input.ConcurrencyStamp)throw new DbUpdateConcurrencyException("Team access changed. Reload before saving.");
        if(input.Status is not("Active" or "Inactive"))throw new InvalidOperationException("Choose Active or Inactive.");
        if(input.Status=="Active"&&user.Status!="Active")await CheckCapacity(db,await db.EmployerWorkspaces.SingleAsync(w=>w.Id==overview.Workspace.Id),user.Id);
        user.Status=input.Status;user.CanCreateClients=input.CanCreateClients;user.CanCreatePositions=input.CanCreatePositions;user.CanExportReports=input.CanExportReports;user.CanViewTalentPool=input.CanViewTalentPool;user.SecurityStamp=Guid.NewGuid().ToString();user.ConcurrencyStamp=Guid.NewGuid().ToString();
        AtsService.Audit(db,overview.Workspace.OwnerId,"Team member access updated",user.Id,$"{user.Status}; clients={user.CanCreateClients}; positions={user.CanCreatePositions}; exports={user.CanExportReports}; pool={user.CanViewTalentPool}");await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
