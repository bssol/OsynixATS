using Osynix.Ats.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
namespace Osynix.Ats.Services;
public class Access(AuthenticationStateProvider state, IDbContextFactory<AtsDbContext>? factory = null)
{
    public const string AdminPermissionClaim = "osynix:admin-permission";
    public const string AdminAccessLevelClaim = "osynix:admin-access-level";
    public static readonly string[] AdminPermissionKeys = [
        "admin.subadmins.view", "admin.subadmins.manage",
        "admin.workspaces.view", "admin.accounts.manage", "admin.requests.review", "admin.subscriptions.manage",
        "admin.rules.view", "admin.rules.manage", "admin.audit.view"
    ];

    public async Task<ClaimsPrincipal> RequireAdminPermission(string permission)
    {
        var user=(await state.GetAuthenticationStateAsync()).User;
        if(user.Identity?.IsAuthenticated!=true || !(user.IsInRole("Admin")||user.IsInRole("SubAdmin")))
            throw new UnauthorizedAccessException("Admin access is required.");
        if(factory is null)throw new UnauthorizedAccessException("Admin access could not be verified.");
        await using var db=await factory.CreateDbContextAsync();
        var id=user.FindFirstValue(ClaimTypes.NameIdentifier);
        var account=await db.Users.FindAsync(id);
        if(account is null||account.Status!="Active")throw new UnauthorizedAccessException("This Admin account is not active.");
        var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==account.Id select role.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(user.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order()))throw new UnauthorizedAccessException("Your permissions changed. Sign in again.");
        if(user.IsInRole("Admin"))return user;
        if(!user.IsInRole("SubAdmin"))throw new UnauthorizedAccessException("Admin access is required.");
        var grants=await db.UserClaims.AsNoTracking().Where(claim=>claim.UserId==account.Id&&(claim.ClaimType==AdminPermissionClaim||claim.ClaimType==AdminAccessLevelClaim)).ToListAsync();
        if(!grants.Any(claim=>claim.ClaimType==AdminPermissionClaim&&claim.ClaimValue==permission))throw new UnauthorizedAccessException("Your Sub-Admin role does not include this permission.");
        var identity=new ClaimsIdentity(user.Identity as ClaimsIdentity);
        foreach(var claim in identity.Claims.Where(claim=>claim.Type==AdminPermissionClaim||claim.Type==AdminAccessLevelClaim).ToList())identity.RemoveClaim(claim);
        foreach(var grant in grants)identity.AddClaim(new Claim(grant.ClaimType!,grant.ClaimValue!));
        return new ClaimsPrincipal(identity);
    }

    public async Task<bool> CanAdmin(string permission)
    {
        try{await RequireAdminPermission(permission);return true;}catch(UnauthorizedAccessException){return false;}
    }

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
        if(factory is not null){await using var context=await factory.CreateDbContextAsync();return WorkspaceContext.Effective(user,await WorkspaceContext.Resolve(context,user));}
        return user;
    }
    public async Task<bool?> PositionMode(AtsDbContext db){var principal=(await state.GetAuthenticationStateAsync()).User;return await ProfileModes.Selected(db,principal,await WorkspaceContext.Resolve(db,principal));}
    public async Task<string> UserId() => (await Require()).FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<Guid?> WorkspaceId(){var raw=(await state.GetAuthenticationStateAsync()).User;await Require();if(factory is null)return null;await using var db=await factory.CreateDbContextAsync();return (await WorkspaceContext.Resolve(db,raw)).Id;}
    public async Task<ActiveWorkspace?> Context(){if(factory is null)return null;await Require();await using var db=await factory.CreateDbContextAsync();return await WorkspaceContext.Resolve(db,(await state.GetAuthenticationStateAsync()).User);}
    public async Task RequirePermission(string permission)
    {
        var user=await Require();
        if(user.IsInRole("Admin")) return;
        if(factory is null)throw new UnauthorizedAccessException("Account permissions could not be verified.");
        await using var db=await factory.CreateDbContextAsync();
        var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));
        var active=await Context();
        if(active is {Legacy:false}){
            var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==active.Id);
            if(permission=="HiringReview.Share"){await SubscriptionRules.RequireHiringReviews(db,workspace.Id);return;}
            if(permission=="Freelancers.View"){if(!active.Owner)throw new UnauthorizedAccessException("Only the workspace owner can browse freelance recruiters.");await SubscriptionRules.RequireFreelancerDirectory(db,workspace.Id);return;}
            if(permission=="EmailTemplates.Manage"){
                if(!workspace.EmailTemplatesEnabled&&SubscriptionRules.Terms(workspace)?.EmailTemplates!=true)throw new UnauthorizedAccessException("Email Templates Access Has Not Been Enabled By Admin For This Workspace.");
                return;
            }
            var clientsAllowed=workspace.Kind==AccountTypes.Freelance||(workspace.AgencyEnabled&&(!active.Owner||await PositionMode(db)==true))||account?.AccountType==AccountTypes.Legacy;
            if(permission is "Clients.View" or "Clients.Create" or "Clients.Delete"){
              if(!clientsAllowed||(!active.Owner&&!active.Membership!.CanCreateClients))throw new UnauthorizedAccessException("Agency Access And Client Permissions Are Required.");
              if(permission=="Clients.View")return;
            }
            var terms=SubscriptionRules.Terms(workspace);
            if(permission=="Reports.Export"&&terms is not null&&!terms.Reports)throw new UnauthorizedAccessException("Report exports require a plan that includes reports.");
            var master=workspace.MasterTalentPoolEnabled||terms?.MasterPool==true;
            var permit=active.Owner?(permission is "Positions.Create" or "Clients.Create" or "Clients.Delete" or "Reports.Export" or "Talent.View" || permission=="Talent.Master"&&master):permission switch{"Positions.Create"=>active.Membership!.CanCreatePositions,"Clients.Create"=>active.Membership!.CanCreateClients,"Reports.Export"=>active.Membership!.CanExportReports,"Talent.View"=>active.Membership!.CanViewTalentPool,"Talent.Master"=>master&&active.Membership!.CanUseMasterPool,_=>false};
            if(!permit)throw new UnauthorizedAccessException("Your Workspace Does Not Have Access To This Feature.");return;
        }
        var allowed=account is not null && (permission switch { "HiringReview.Share"=>account.AccountType==AccountTypes.Legacy,"Positions.Create"=>account.CanCreatePositions,"Clients.Create"=>account.CanCreateClients,"Talent.View"=>account.EmployerWorkspaceId is Guid team?account.CanViewTalentPool&&db.EmployerWorkspaces.Any(w=>w.Id==team&&w.TeamTalentPoolEnabled):account.TalentPoolPaidUntilUtc>DateTime.UtcNow,"Reports.Export"=>account.CanExportReports,_=>false });
        if(!allowed) throw new UnauthorizedAccessException("Your account does not have permission for this action.");
    }
    public async Task<bool> Can(string permission){try{await RequirePermission(permission);return true;}catch(UnauthorizedAccessException){return false;}}
    public async Task RequireTalentCandidate(Guid id){
        if(factory is null){await RequirePermission("Talent.View");return;}
        var user=await Require();await using var db=await factory.CreateDbContextAsync();if(user.IsInRole("Admin"))return;
        var workspaceId=await WorkspaceId();var active=await Context();
        if(active is {Legacy:false}){if(await Can("Talent.View")&&await (await WorkspaceTalent.Query(db,user,active)).AnyAsync(c=>c.Id==id))return;if(await Can("Talent.Master")&&await TalentIdentity.MasterCandidates(db).AnyAsync(c=>c.Id==id))return;throw new UnauthorizedAccessException("This Profile Is Not Available In Your Workspace Or Subscription.");}
        await RequirePermission("Talent.View");if(!await EndpointAccess.PoolCandidates(db,workspaceId).AnyAsync(c=>c.Id==id))throw new InvalidOperationException("Candidate Not Found.");
    }
    public static bool Scoped(ClaimsPrincipal user)=>!user.IsInRole("Admin")&&(user.IsInRole("Recruiter")||user.IsInRole("Employer"));
    public async Task RequirePosition(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();
        var mode=await PositionMode(db);if(!await db.Positions.AnyAsync(x=>x.Id==id&&(mode==null||x.IsAgencyPosition==mode)&&((user.IsInRole("Employer")||(x.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==x.Id&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name))))&&x.WorkspaceId==workspaceId)))throw new UnauthorizedAccessException("This position is not assigned to your account.");
    }
    public async Task RequireCandidate(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();var uid=user.FindFirstValue(ClaimTypes.NameIdentifier);
        var mode=await PositionMode(db);if(!await db.Assessments.AnyAsync(x=>x.CandidateId==id&&(mode==null||x.Position!.IsAgencyPosition==mode)&&((user.IsInRole("Employer")||(x.Position!.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==x.PositionId&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name))))&&x.Position!.WorkspaceId==workspaceId)))throw new UnauthorizedAccessException("This candidate is not in your workspace.");
    }
    public async Task RequireApplication(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();var uid=user.FindFirstValue(ClaimTypes.NameIdentifier);
        var mode=await PositionMode(db);if(!await db.Assessments.AnyAsync(x=>x.Id==id&&(mode==null||x.Position!.IsAgencyPosition==mode)&&((user.IsInRole("Employer")||(x.Position!.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==x.PositionId&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name))))&&x.Position!.WorkspaceId==workspaceId)))throw new UnauthorizedAccessException("This application is not in your workspace.");
    }    public async Task RequireClient(Guid id){
        var user=await Require();if(!Scoped(user)||factory is null)return;
        await using var db=await factory.CreateDbContextAsync();var name=user.Identity!.Name;var workspaceId=await WorkspaceId();var uid=user.FindFirstValue(ClaimTypes.NameIdentifier);var key=id.ToString();
        if(workspaceId is not null){if(!await db.Clients.AnyAsync(c=>c.Id==id&&c.WorkspaceId==workspaceId))throw new UnauthorizedAccessException("This client is not in your workspace.");return;}
        if(!await db.Clients.AnyAsync(c=>c.Id==id&&c.WorkspaceId==workspaceId)||(!await db.Positions.AnyAsync(p=>p.ClientId==id&&((p.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==p.Id&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name)))))&&!await db.Audit.AnyAsync(a=>a.EntityId==key&&a.UserId==uid&&a.Action=="Client saved")))throw new UnauthorizedAccessException("This client is not in your workspace.");
    }}
