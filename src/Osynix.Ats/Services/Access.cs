using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
namespace Osynix.Ats.Services;
public class Access(AuthenticationStateProvider state, IDbContextFactory<AtsDbContext>? factory = null)
{
    public async Task<ClaimsPrincipal> Require(bool admin = false)
    {
        var user = (await state.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || !(user.IsInRole("Admin") || (!admin && (user.IsInRole("Recruiter")||user.IsInRole("Employer")))))
            throw new UnauthorizedAccessException("You do not have permission for this action.");
        if(factory is null&&user.IsInRole("Employer")&&!user.IsInRole("Admin"))throw new UnauthorizedAccessException("Employer Workspace Access Could Not Be Verified.");
        if(factory is not null) {
            await using var db=await factory.CreateDbContextAsync();
            var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));
            if(account is null || account.Status!="Active" || (user.IsInRole("Employer")&&!account.EmailConfirmed) || !await EndpointAccess.TeamActive(db,account)) throw new UnauthorizedAccessException("This account is not active.");
            var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==account.Id select role.Name!).ToListAsync();
            if(!roles.Order().SequenceEqual(user.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order())) throw new UnauthorizedAccessException("Your permissions changed. Sign in again.");
        }
        return user;
    }
    public async Task<string> UserId() => (await Require()).FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<Guid?> WorkspaceId(){var user=await Require();if(factory is null)return null;await using var db=await factory.CreateDbContextAsync();var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));return user.IsInRole("Employer")&&!user.IsInRole("Admin")?(await EmployerService.EnsureWorkspace(db,account!)).Id:account?.EmployerWorkspaceId;}
    public async Task RequirePermission(string permission)
    {
        var user=await Require();
        if(user.IsInRole("Admin")) return;
        if(factory is null)throw new UnauthorizedAccessException("Account permissions could not be verified.");
        await using var db=await factory.CreateDbContextAsync();
        var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));
        if(user.IsInRole("Employer")){var workspaceId=await WorkspaceId();var ownerAllowed=permission is "Positions.Create" or "Clients.Create" or "Clients.Delete" or "Reports.Export" || (permission=="Talent.View"&&await db.EmployerWorkspaces.AnyAsync(w=>w.Id==workspaceId&&w.TeamTalentPoolEnabled));if(!ownerAllowed)throw new UnauthorizedAccessException("Your Workspace Does Not Have Access To This Feature.");return;}
        var allowed=account is not null && (permission switch { "Positions.Create"=>account.CanCreatePositions,"Clients.Create"=>account.CanCreateClients,"Talent.View"=>account.EmployerWorkspaceId is Guid team?account.CanViewTalentPool&&db.EmployerWorkspaces.Any(w=>w.Id==team&&w.TeamTalentPoolEnabled):account.TalentPoolPaidUntilUtc>DateTime.UtcNow,"Reports.Export"=>account.CanExportReports,_=>false });
        if(!allowed) throw new UnauthorizedAccessException("Your account does not have permission for this action.");
    }
    public async Task<bool> Can(string permission){try{await RequirePermission(permission);return true;}catch(UnauthorizedAccessException){return false;}}
    public async Task RequireTalentCandidate(Guid id){await RequirePermission("Talent.View");if(factory is null)return;await using var db=await factory.CreateDbContextAsync();var user=await Require();var workspaceId=await WorkspaceId();var query=user.IsInRole("Admin")?db.Candidates:EndpointAccess.PoolCandidates(db,workspaceId);if(!await query.AnyAsync(c=>c.Id==id))throw new InvalidOperationException("Candidate not found.");}
    public static bool Scoped(ClaimsPrincipal user)=>!user.IsInRole("Admin")&&(user.IsInRole("Recruiter")||user.IsInRole("Employer"));
    public async Task RequirePosition(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();
        if(!await db.Positions.AnyAsync(x=>x.Id==id&&((user.IsInRole("Employer")||x.AssignedRecruiter==name)&&x.WorkspaceId==workspaceId)))throw new UnauthorizedAccessException("This position is not assigned to your account.");
    }
    public async Task RequireCandidate(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();var uid=user.FindFirstValue(ClaimTypes.NameIdentifier);
        if(!await db.Assessments.AnyAsync(x=>x.CandidateId==id&&((user.IsInRole("Employer")||x.Position!.AssignedRecruiter==name)&&x.Position!.WorkspaceId==workspaceId)))throw new UnauthorizedAccessException("This candidate is not in your workspace.");
    }
    public async Task RequireApplication(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();var uid=user.FindFirstValue(ClaimTypes.NameIdentifier);
        if(!await db.Assessments.AnyAsync(x=>x.Id==id&&((user.IsInRole("Employer")||x.Position!.AssignedRecruiter==name)&&x.Position!.WorkspaceId==workspaceId)))throw new UnauthorizedAccessException("This application is not in your workspace.");
    }    public async Task RequireClient(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();var uid=user.FindFirstValue(ClaimTypes.NameIdentifier);var key=id.ToString();
        if(workspaceId is not null){if(!await db.Clients.AnyAsync(c=>c.Id==id&&c.WorkspaceId==workspaceId))throw new UnauthorizedAccessException("This client is not in your workspace.");return;}
        if(!await db.Clients.AnyAsync(c=>c.Id==id&&c.WorkspaceId==workspaceId)||(!await db.Positions.AnyAsync(p=>p.ClientId==id&&(p.AssignedRecruiter==name))&&!await db.Audit.AnyAsync(a=>a.EntityId==key&&a.UserId==uid&&a.Action=="Client saved")))throw new UnauthorizedAccessException("This client is not in your workspace.");
    }}
