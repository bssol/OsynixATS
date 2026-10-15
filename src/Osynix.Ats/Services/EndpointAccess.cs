using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;

namespace Osynix.Ats.Services;

public static class EndpointAccess
{
    public static async Task<Guid?> WorkspaceId(AtsDbContext db,ClaimsPrincipal user){var id=user.FindFirstValue(ClaimTypes.NameIdentifier);return user.IsInRole("Employer")&&!user.IsInRole("Admin")?(await db.EmployerWorkspaces.Where(w=>w.OwnerId==id).Select(w=>(Guid?)w.Id).SingleOrDefaultAsync()??Guid.Empty):(await db.Users.FindAsync(id))?.EmployerWorkspaceId;}
    public static IQueryable<Osynix.Ats.Domain.Candidate> PoolCandidates(AtsDbContext db,Guid? workspaceId)=>db.Candidates.Where(c=>!db.Assessments.Any(a=>a.CandidateId==c.Id)||db.Assessments.Any(a=>a.CandidateId==c.Id&&(a.Position!.WorkspaceId==null||a.Position.WorkspaceId==workspaceId)));
    public static Task<bool> TeamActive(AtsDbContext db,AtsUser account)=>account.EmployerWorkspaceId is not Guid id?Task.FromResult(true):db.EmployerWorkspaces.AnyAsync(w=>w.Id==id&&db.Users.Any(u=>u.Id==w.OwnerId&&u.Status=="Active"&&u.EmailConfirmed)&&db.UserRoles.Any(ur=>ur.UserId==w.OwnerId&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Employer")));
    public static async Task<bool> Allowed(AtsDbContext db,ClaimsPrincipal principal,string? permission=null)
    {
        var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);if(id is null) return false;
        var account=await db.Users.FindAsync(id);if(account is null||account.Status!="Active"||!await TeamActive(db,account)) return false;
        var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==id select role.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order())) return false;
        if(roles.Contains("Admin")) return true;
        if(roles.Contains("Employer")){if(!account.EmailConfirmed)return false;var workspaceId=await WorkspaceId(db,principal);return permission is null or "Reports.Export" or "Positions.Create" or "Clients.Create" ||(permission=="Talent.View"&&await db.EmployerWorkspaces.AnyAsync(w=>w.Id==workspaceId&&w.TeamTalentPoolEnabled));}
        return roles.Contains("Recruiter")&&(permission switch {null=>true,"Reports.Export"=>account.CanExportReports,"Talent.View"=>account.EmployerWorkspaceId is Guid team?account.CanViewTalentPool&&db.EmployerWorkspaces.Any(w=>w.Id==team&&w.TeamTalentPoolEnabled):account.TalentPoolPaidUntilUtc>DateTime.UtcNow,"Positions.Create"=>account.CanCreatePositions,"Clients.Create"=>account.CanCreateClients,_=>false});
    }
}
