using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public static class EndpointAccess
{
    public static async Task<Guid?> WorkspaceId(AtsDbContext db,ClaimsPrincipal user)=>(await WorkspaceContext.Resolve(db,user)).Id;
    public static IQueryable<Osynix.Ats.Domain.Candidate> PoolCandidates(AtsDbContext db,Guid? workspaceId)=>db.Candidates.Where(c=>!db.Assessments.Any(a=>a.CandidateId==c.Id)||db.Assessments.Any(a=>a.CandidateId==c.Id&&(a.Position!.WorkspaceId==null||a.Position.WorkspaceId==workspaceId)));
    public static Task<bool> TeamActive(AtsDbContext db,AtsUser account)=>account.AccountType!=AccountTypes.Legacy?Task.FromResult(true):account.EmployerWorkspaceId is not Guid id?Task.FromResult(true):db.EmployerWorkspaces.AnyAsync(w=>w.Id==id&&db.Users.Any(u=>u.Id==w.OwnerId&&u.Status=="Active"&&u.EmailConfirmed)&&db.UserRoles.Any(ur=>ur.UserId==w.OwnerId&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Employer")));
    public static async Task<bool> Allowed(AtsDbContext db,ClaimsPrincipal principal,string? permission=null)
    {
        var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);if(id is null) return false;
        var account=await db.Users.FindAsync(id);if(account is null||account.Status!="Active"||!await TeamActive(db,account)) return false;
        var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==id select role.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order())) return false;
        if(roles.Contains("Admin")) return true;
        ActiveWorkspace active;try{active=await WorkspaceContext.Resolve(db,principal);}catch(UnauthorizedAccessException){return false;}
        if(!active.Legacy){if(permission is "Clients.View" or "Clients.Create" or "Clients.Delete"){var clientsAllowed=account.AccountType==AccountTypes.Legacy||await db.EmployerWorkspaces.AnyAsync(w=>w.Id==active.Id&&(w.Kind==AccountTypes.Freelance||w.AgencyEnabled));if(clientsAllowed&&active.Owner&&account.AccountType==AccountTypes.Employer&&await ProfileModes.Selected(db,principal,active)!=true)clientsAllowed=false;if(!clientsAllowed||(!active.Owner&&!active.Membership!.CanCreateClients))return false;if(permission=="Clients.View")return true;}var subscriptionWorkspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==active.Id);var planTerms=SubscriptionRules.Terms(subscriptionWorkspace);if(permission=="Reports.Export"&&planTerms is not null&&!planTerms.Reports)return false;var master=subscriptionWorkspace.MasterTalentPoolEnabled||planTerms?.MasterPool==true;return active.Owner?(permission is null or "Reports.Export" or "Positions.Create" or "Clients.Create" or "Talent.View"||permission=="Talent.Master"&&master):permission switch{null=>true,"Reports.Export"=>active.Membership!.CanExportReports,"Talent.View"=>active.Membership!.CanViewTalentPool,"Talent.Master"=>master&&active.Membership!.CanUseMasterPool,"Positions.Create"=>active.Membership!.CanCreatePositions,"Clients.Create"=>active.Membership!.CanCreateClients,_=>false};}
        if(roles.Contains("Employer")){if(!account.EmailConfirmed)return false;var workspaceId=await WorkspaceId(db,principal);return permission is null or "Reports.Export" or "Positions.Create" or "Clients.Create" ||(permission=="Talent.View"&&await db.EmployerWorkspaces.AnyAsync(w=>w.Id==workspaceId&&w.TeamTalentPoolEnabled));}
        return roles.Contains("Recruiter")&&(permission switch {null=>true,"Reports.Export"=>account.CanExportReports,"Talent.View"=>account.EmployerWorkspaceId is Guid team?account.CanViewTalentPool&&db.EmployerWorkspaces.Any(w=>w.Id==team&&w.TeamTalentPoolEnabled):account.TalentPoolPaidUntilUtc>DateTime.UtcNow,"Positions.Create"=>account.CanCreatePositions,"Clients.Create"=>account.CanCreateClients,_=>false});
    }
}
