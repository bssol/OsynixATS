using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using System.Text.Json;
namespace Osynix.Ats.Services;
public record WorkspaceUser(string Id,string Username,string Email,string FullName,string Role,string Status,bool CanCreatePositions,bool CanViewTalentPool,bool CanExportReports,bool HasPassword,bool CanCreateClients=false,DateTime? TalentPoolPaidUntilUtc=null,Guid? EmployerWorkspaceId=null,string AccountType="Legacy");
public record SubAdminAccount(string Id,string Email,string FullName,string Status,string AccessLevel,List<string> Permissions);
public record AdminActivitySummary(DateTime CreatedUtc,string Action,string Account,string AccountInfo,string Details);
public class AdminService(Access access,IServiceScopeFactory scopes,IDbContextFactory<AtsDbContext> factory)
{
    static readonly Dictionary<string,string> AdminPermissionLabels=new(){
        ["admin.subadmins.view"]="View Sub-Admin Directory",
        ["admin.subadmins.manage"]="Add, Edit And Deactivate Sub-Admins",
        ["admin.workspaces.view"]="View Employer, Agency And Freelance Recruiter Workspaces",
        ["admin.accounts.manage"]="Update Workspace Owner Accounts And Reset Passwords",
        ["admin.requests.review"]="Review And Respond To Access Requests",
        ["admin.subscriptions.manage"]="Grant Or Revoke Subscription Entitlements",
        ["admin.rules.view"]="View Assessment Decision Rules",
        ["admin.rules.manage"]="Change Assessment Decision Rules",
        ["admin.audit.view"]="View Admin Audit Events"
    };

    public IReadOnlyDictionary<string,string> PermissionOptions=>AdminPermissionLabels;

    public async Task<List<WorkspaceUser>> WorkspaceOwners()
    {
        await access.RequireAdminPermission("admin.workspaces.view");
        await using var db=await factory.CreateDbContextAsync();
        var owners=await db.Users.AsNoTracking().Where(user=>db.EmployerWorkspaces.Any(workspace=>workspace.OwnerId==user.Id)&&!db.UserRoles.Any(ur=>ur.UserId==user.Id&&db.Roles.Any(role=>role.Id==ur.RoleId&&(role.Name=="Admin"||role.Name=="SubAdmin")))).OrderBy(user=>user.FullName).ToListAsync();
        var ids=owners.Select(user=>user.Id).ToList();
        var roles=await (from ur in db.UserRoles.AsNoTracking() join role in db.Roles.AsNoTracking() on ur.RoleId equals role.Id where ids.Contains(ur.UserId) select new{ur.UserId,role.Name}).ToListAsync();
        return owners.Select(user=>new WorkspaceUser(user.Id,user.UserName??"",user.Email??"",user.FullName,string.Join(", ",roles.Where(role=>role.UserId==user.Id).Select(role=>role.Name)),user.Status,user.CanCreatePositions,user.CanViewTalentPool,user.CanExportReports,user.PasswordHash is not null,user.CanCreateClients,user.TalentPoolPaidUntilUtc,user.EmployerWorkspaceId,user.AccountType)).ToList();
    }

    static async Task<AtsUser> WorkspaceOwner(AtsDbContext db,Guid workspaceId)
    {
        var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(workspace=>workspace.Id==workspaceId)??throw new InvalidOperationException("Workspace not found.");
        var owner=await db.Users.SingleAsync(user=>user.Id==workspace.OwnerId);
        var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==owner.Id select role.Name!).ToListAsync();
        if(roles.Any(role=>role is "Admin" or "SubAdmin")||!roles.Any(role=>role is "Employer" or "Recruiter"))throw new UnauthorizedAccessException("Only Employer and Freelance Recruiter workspace owners can be managed here.");
        return owner;
    }

    public async Task UpdateWorkspaceOwner(Guid workspaceId,string fullName,string status)
    {
        var actor=await access.RequireAdminPermission("admin.accounts.manage");
        await access.RequireAdminPermission("admin.workspaces.view");
        if(string.IsNullOrWhiteSpace(fullName)||fullName.Trim().Length>160)throw new InvalidOperationException("Enter a name of up to 160 characters.");
        if(status is not("Active" or "Inactive" or "Pending"))throw new InvalidOperationException("Choose a valid account status.");
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
        await using var tx=await db.Database.BeginTransactionAsync();var owner=await WorkspaceOwner(db,workspaceId);
        var before=$"Name: {owner.FullName}; Status: {owner.Status}";
        owner.FullName=fullName.Trim();owner.Status=status;Check(await manager.UpdateAsync(owner));Check(await manager.UpdateSecurityStampAsync(owner));
        AtsService.Audit(db,actor.Identity!.Name!,"Workspace owner account updated",owner.Id,$"{before} → Name: {owner.FullName}; Status: {owner.Status}");await db.SaveChangesAsync();await tx.CommitAsync();
    }

    public async Task ResetWorkspaceOwnerPassword(Guid workspaceId,string password)
    {
        var actor=await access.RequireAdminPermission("admin.accounts.manage");
        await access.RequireAdminPermission("admin.workspaces.view");
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
        await using var tx=await db.Database.BeginTransactionAsync();var owner=await WorkspaceOwner(db,workspaceId);
        Check(await manager.ResetPasswordAsync(owner,await manager.GeneratePasswordResetTokenAsync(owner),password));
        Check(await manager.ResetAccessFailedCountAsync(owner));if(await manager.GetLockoutEnabledAsync(owner))Check(await manager.SetLockoutEndDateAsync(owner,null));
        AtsService.Audit(db,actor.Identity!.Name!,"Workspace owner password reset",owner.Id,"The workspace owner's ATS password was reset by an administrator.");await db.SaveChangesAsync();await tx.CommitAsync();
    }

    public async Task<List<SubAdminAccount>> SubAdmins()
    {
        await access.RequireAdminPermission("admin.subadmins.view");
        await using var db=await factory.CreateDbContextAsync();
        var users=await (from user in db.Users.AsNoTracking()
                         join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                         join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                         where role.Name=="SubAdmin" orderby user.FullName,user.Email select user).ToListAsync();
        var ids=users.Select(user=>user.Id).ToList();
        var claims=await db.UserClaims.AsNoTracking().Where(claim=>ids.Contains(claim.UserId)&&
            (claim.ClaimType==Access.AdminPermissionClaim||claim.ClaimType==Access.AdminAccessLevelClaim)).ToListAsync();
        return users.Select(user=>new SubAdminAccount(user.Id,user.Email??"",user.FullName,user.Status,
            claims.FirstOrDefault(claim=>claim.UserId==user.Id&&claim.ClaimType==Access.AdminAccessLevelClaim)?.ClaimValue??"View Only",
            claims.Where(claim=>claim.UserId==user.Id&&claim.ClaimType==Access.AdminPermissionClaim).Select(claim=>claim.ClaimValue??"").ToList())).ToList();
    }

    public async Task CreateSubAdmin(string email,string fullName,string password,string accessLevel,List<string> permissions)
    {
        var actor=await access.RequireAdminPermission("admin.subadmins.manage");
        if(!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))throw new InvalidOperationException("Enter a valid email address.");
        if(string.IsNullOrWhiteSpace(fullName)||fullName.Trim().Length>160)throw new InvalidOperationException("Enter the Sub-Admin's full name.");
        ValidateAdminAccess(actor,accessLevel,permissions);
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
        var user=new AtsUser{UserName=email.Trim(),Email=email.Trim(),EmailConfirmed=true,FullName=fullName.Trim(),AccountType="SubAdmin",Status="Active"};
        Check(await manager.CreateAsync(user,password));Check(await manager.AddToRoleAsync(user,"SubAdmin"));
        await ReplaceAdminClaims(manager,user,accessLevel,permissions);
        AtsService.Audit(db,actor.Identity!.Name!,"Sub-Admin created",user.Id,$"{accessLevel}; {string.Join(",",permissions)}");
        await db.SaveChangesAsync();
    }

    public async Task UpdateSubAdmin(SubAdminAccount input)
    {
        var actor=await access.RequireAdminPermission("admin.subadmins.manage");
        if(input.Status is not("Active" or "Inactive"))throw new InvalidOperationException("Choose Active or Inactive status.");
        ValidateAdminAccess(actor,input.AccessLevel,input.Permissions);
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
        var user=await manager.FindByIdAsync(input.Id)??throw new InvalidOperationException("Sub-Admin not found.");
        if(!await manager.IsInRoleAsync(user,"SubAdmin"))throw new InvalidOperationException("Only Sub-Admin accounts can be managed here.");
        if(user.Id==actor.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value&&input.Status!="Active")throw new InvalidOperationException("You cannot deactivate your own Admin account.");
        var before=await AdminClaims(manager,user);
        user.FullName=input.FullName.Trim();user.Status=input.Status;Check(await manager.UpdateAsync(user));
        await ReplaceAdminClaims(manager,user,input.AccessLevel,input.Permissions);Check(await manager.UpdateSecurityStampAsync(user));
        var after=await AdminClaims(manager,user);
        AtsService.Audit(db,actor.Identity!.Name!,"Sub-Admin access updated",user.Id,$"{before.AccessLevel} → {after.AccessLevel}; {string.Join(",",after.Permissions)}");
        await db.SaveChangesAsync();
    }

    static void ValidateAdminAccess(System.Security.Claims.ClaimsPrincipal actor,string level,List<string> permissions)
    {
        if(level is not("Full Control" or "Partial Control" or "View Only"))throw new InvalidOperationException("Choose Full Control, Partial Control or View Only.");
        if(permissions.Any(permission=>!AdminPermissionLabels.ContainsKey(permission)))throw new InvalidOperationException("The selected Admin permissions are invalid.");
        if(level=="Full Control"&&!Access.AdminPermissionKeys.All(permissions.Contains))throw new InvalidOperationException("Full Control must include all Admin permissions.");
        if(level=="View Only"&&permissions.Any(permission=>permission.EndsWith(".manage",StringComparison.Ordinal)||permission is "admin.requests.review" or "admin.subscriptions.manage"))throw new InvalidOperationException("View Only accounts cannot receive change permissions.");
        if(permissions.Contains("admin.subadmins.manage")&&!permissions.Contains("admin.subadmins.view")||permissions.Contains("admin.rules.manage")&&!permissions.Contains("admin.rules.view"))throw new InvalidOperationException("A change permission also requires its matching view permission.");
        if(permissions.Any(permission=>permission is "admin.accounts.manage" or "admin.requests.review" or "admin.subscriptions.manage")&&!permissions.Contains("admin.workspaces.view"))throw new InvalidOperationException("Workspace actions require an explicit View Workspaces permission as well.");
        if(!actor.IsInRole("Admin")){
            if(permissions.Any(permission=>!actor.HasClaim(Access.AdminPermissionClaim,permission)))throw new InvalidOperationException("You cannot grant permissions that you do not hold.");
            if(level=="Full Control"&&!actor.HasClaim(Access.AdminAccessLevelClaim,"Full Control"))throw new InvalidOperationException("Only a Full Control Admin can grant Full Control.");
        }
    }

    static async Task ReplaceAdminClaims(UserManager<AtsUser> manager,AtsUser user,string level,List<string> permissions)
    {
        var existing=await manager.GetClaimsAsync(user);
        var owned=existing.Where(claim=>claim.Type is Access.AdminPermissionClaim or Access.AdminAccessLevelClaim).ToList();
        if(owned.Count>0)Check(await manager.RemoveClaimsAsync(user,owned));
        var claims=permissions.Distinct().Select(permission=>new System.Security.Claims.Claim(Access.AdminPermissionClaim,permission)).ToList();
        claims.Add(new System.Security.Claims.Claim(Access.AdminAccessLevelClaim,level));
        Check(await manager.AddClaimsAsync(user,claims));
    }

    static async Task<(string AccessLevel,List<string> Permissions)> AdminClaims(UserManager<AtsUser> manager,AtsUser user)
    {
        var claims=await manager.GetClaimsAsync(user);
        return (claims.FirstOrDefault(claim=>claim.Type==Access.AdminAccessLevelClaim)?.Value??"View Only",claims.Where(claim=>claim.Type==Access.AdminPermissionClaim).Select(claim=>claim.Value).ToList());
    }

    public async Task<List<AuditEntry>> AdminAudit()
    {
        await access.RequireAdminPermission("admin.audit.view");await using var db=await factory.CreateDbContextAsync();
        var adminIds=await (from ur in db.UserRoles.AsNoTracking() join role in db.Roles.AsNoTracking() on ur.RoleId equals role.Id where role.Name=="Admin"||role.Name=="SubAdmin" select ur.UserId).Distinct().ToListAsync();
        return await db.Audit.AsNoTracking().Where(entry=>adminIds.Contains(entry.UserId)||db.Users.Any(user=>adminIds.Contains(user.Id)&&(user.UserName==entry.UserId||user.Email==entry.UserId||user.FullName==entry.ActorName))).OrderByDescending(entry=>entry.CreatedUtc).Take(200).ToListAsync();
    }

    public async Task<List<WorkspaceUser>> Users()
    {
        await access.Require(true);using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var result=new List<WorkspaceUser>();
        foreach(var u in await manager.Users.OrderBy(x=>x.UserName).ToListAsync()) result.Add(new(u.Id,u.UserName??"",u.Email??"",u.FullName,string.Join(", ",await manager.GetRolesAsync(u)),u.Status,u.CanCreatePositions,u.CanViewTalentPool,u.CanExportReports,await manager.HasPasswordAsync(u),u.CanCreateClients,u.TalentPoolPaidUntilUtc,u.EmployerWorkspaceId,u.AccountType));
        return result;
    }
    static void Check(IdentityResult result) {if(!result.Succeeded)throw new InvalidOperationException(string.Join("; ",result.Errors.Select(e=>e.Description)));}
    public async Task CreateUser(string email,string password,string role)
    {
        var admin=await access.Require(true);if(role is not ("Admin" or "Recruiter" or "Employer" or "Candidate"))throw new InvalidOperationException("Invalid role.");
        if(!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))throw new InvalidOperationException("Enter a valid email.");
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();await using var tx=await db.Database.BeginTransactionAsync();
        var user=new AtsUser{AccountType=role=="Recruiter"?AccountTypes.Freelance:role,Email=email.Trim(),UserName=email.Trim(),EmailConfirmed=true};Check(await manager.CreateAsync(user,password));Check(await manager.AddToRoleAsync(user,role));
        AtsService.Audit(db,admin.Identity!.Name!,"User created",user.Id,role);await db.SaveChangesAsync();await tx.CommitAsync();
    }
    public async Task UpdateUser(WorkspaceUser input)
    {
        var admin=await access.Require(true);var uid=await access.UserId();
        if(input.Role is not ("Admin" or "Recruiter" or "Employer" or "Candidate")||input.Status is not ("Active" or "Inactive" or "Pending"))throw new InvalidOperationException("Choose a valid role and account status.");
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();await using var tx=await db.Database.BeginTransactionAsync();
        var u=await manager.FindByIdAsync(input.Id)??throw new InvalidOperationException("User not found.");var roles=await manager.GetRolesAsync(u);
        var previousAccess=JsonSerializer.Serialize(new UserAccessSnapshot(u.FullName,string.Join(", ",roles),u.Status,u.CanCreatePositions,u.CanCreateClients,u.CanViewTalentPool,u.CanExportReports,u.TalentPoolPaidUntilUtc));
        if(u.Id==uid&&(input.Status!="Active"||input.Role!="Admin"))throw new InvalidOperationException("Keep your own administrator account active. Another administrator can change it.");
        if(u.EmployerWorkspaceId is Guid team){
            if(input.Role!="Recruiter")throw new InvalidOperationException("A Team Member Must Remain A Recruiter. Review Workspace Membership Before Changing Their Role.");
            if(input.Status=="Active"&&u.Status!="Active"){
                var limits=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==team);var used=await db.Users.CountAsync(x=>x.EmployerWorkspaceId==team&&x.Status=="Active"&&x.Id!=u.Id);
                if(used>=limits.FreeRecruiterSeats+limits.AdditionalRecruiterSeats)throw new InvalidOperationException("Approve Additional Employer Seats Before Reactivating This Recruiter.");
            }
        }
        if(u.AccountType!=AccountTypes.Legacy&&input.Role!=roles.SingleOrDefault()){
            if(u.AccountType==AccountTypes.Employee||await db.WorkspaceMemberships.AnyAsync(m=>m.UserId==u.Id)||await db.EmployerWorkspaces.AnyAsync(w=>w.OwnerId==u.Id))throw new InvalidOperationException("Use The Workspace Subscription Upgrade Flow For Operational Account Changes.");
            u.AccountType=input.Role=="Recruiter"?AccountTypes.Freelance:input.Role;
        }
        if(input.Status=="Active"&&u.Status!="Active"&&u.AccountType is not(AccountTypes.Freelance or AccountTypes.Employer))foreach(var membership in await db.WorkspaceMemberships.Where(m=>m.UserId==u.Id&&m.Status=="Active").ToListAsync()){
            var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==membership.WorkspaceId);var occupied=await db.WorkspaceMemberships.CountAsync(m=>m.WorkspaceId==w.Id&&m.UserId!=u.Id&&m.Status=="Active"&&db.Users.Any(a=>a.Id==m.UserId&&a.Status=="Active"&&a.AccountType!=AccountTypes.Freelance&&a.AccountType!=AccountTypes.Employer));if(occupied>=w.FreeRecruiterSeats+w.AdditionalRecruiterSeats)throw new InvalidOperationException("An Employer Team Has No Available Seat. Deactivate That Membership Or Increase Its Allowance Before Reactivating This Account.");
        }
        u.FullName=input.FullName.Trim();u.Status=input.Status;u.CanCreatePositions=input.CanCreatePositions;u.CanCreateClients=input.CanCreateClients;u.TalentPoolPaidUntilUtc=input.Role=="Recruiter"&&input.TalentPoolPaidUntilUtc is DateTime paid?DateTime.SpecifyKind(paid,DateTimeKind.Utc):null;u.CanViewTalentPool=u.EmployerWorkspaceId is null?u.TalentPoolPaidUntilUtc>DateTime.UtcNow:input.CanViewTalentPool;u.CanExportReports=input.CanExportReports;
        Check(await manager.UpdateAsync(u));if(!roles.SequenceEqual(new[]{input.Role})){Check(await manager.RemoveFromRolesAsync(u,roles));Check(await manager.AddToRoleAsync(u,input.Role));}
        Check(await manager.UpdateSecurityStampAsync(u));
        var currentAccess=JsonSerializer.Serialize(new UserAccessSnapshot(u.FullName,input.Role,u.Status,u.CanCreatePositions,u.CanCreateClients,u.CanViewTalentPool,u.CanExportReports,u.TalentPoolPaidUntilUtc));
        AtsService.Audit(db,admin.Identity!.Name!,"User access updated",u.Id,"Account access settings updated.",null,null,previousAccess,currentAccess);await db.SaveChangesAsync();await tx.CommitAsync();
    }
    public async Task ResetPassword(string id,string password)
    {
        var admin=await access.Require(true);using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var u=await manager.FindByIdAsync(id)??throw new InvalidOperationException("User not found.");
        Check(await manager.ResetPasswordAsync(u,await manager.GeneratePasswordResetTokenAsync(u),password));await manager.ResetAccessFailedCountAsync(u);await manager.SetLockoutEndDateAsync(u,null);
        await using var db=await factory.CreateDbContextAsync();AtsService.Audit(db,admin.Identity!.Name!,"Password reset",u.Id,"Administrator reset the account password.");await db.SaveChangesAsync();
    }
    public async Task<List<AuditEntry>> Audit()
    {await access.Require(true);await using var db=await factory.CreateDbContextAsync();return await db.Audit.AsNoTracking().OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync();}
    public async Task<List<AdminActivitySummary>> WorkspaceActivity(Guid workspaceId)
    {
        await access.RequireAdminPermission("admin.workspaces.view");
        await access.RequireAdminPermission("admin.audit.view");
        await using var db=await factory.CreateDbContextAsync();
        var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==workspaceId);
        var requests=await db.EmployerAccessRequests.AsNoTracking().Where(r=>r.WorkspaceId==workspaceId).ToListAsync();
        var ids=requests.Select(r=>r.Id.ToString()).Append(workspaceId.ToString()).Append(workspace.OwnerId).ToList();
        var entries=await db.Audit.AsNoTracking().Where(e=>ids.Contains(e.EntityId)).OrderByDescending(e=>e.CreatedUtc).Take(100).ToListAsync();
        var actors=entries.Select(e=>e.UserId).Distinct().ToList();
        var users=await db.Users.AsNoTracking().Where(u=>actors.Contains(u.Id)||actors.Contains(u.UserName! )||actors.Contains(u.Email!)).ToListAsync();
        return entries.Select(e=>new AdminActivitySummary(e.CreatedUtc,ReadableAction(e.Action),workspace.CompanyName,
            users.FirstOrDefault(u=>u.Id==e.UserId||u.UserName==e.UserId||u.Email==e.UserId)?.FullName??(string.IsNullOrWhiteSpace(e.ActorName)?"System":e.ActorName),
            ReadableDetails(e,requests.FirstOrDefault(r=>r.Id.ToString()==e.EntityId)))).ToList();
    }
    public async Task<List<AdminActivitySummary>> RecentAdminActivity()
    {
        await access.Require(true);
        await using var db=await factory.CreateDbContextAsync();
        var adminIds=await (from userRole in db.UserRoles.AsNoTracking()
                            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                            where role.Name=="Admin"||role.Name=="SubAdmin"
                            select userRole.UserId).Distinct().ToListAsync();
        var accounts=await db.Users.AsNoTracking().Where(user=>adminIds.Contains(user.Id))
            .Select(user=>new{user.Id,user.UserName,user.Email,user.FullName}).ToListAsync();
        var actorNames=accounts.SelectMany(user=>new[]{user.UserName,user.Email,user.FullName})
            .Where(name=>!string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        var entries=await db.Audit.AsNoTracking()
            .Where(entry=>adminIds.Contains(entry.UserId)||actorNames.Contains(entry.UserId)||actorNames.Contains(entry.ActorName))
            .OrderByDescending(entry=>entry.CreatedUtc).Take(100).ToListAsync();
        var targetIds=entries.Select(entry=>entry.EntityId).Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var targetUsers=await db.Users.AsNoTracking().Where(user=>targetIds.Contains(user.Id)).ToListAsync();
        var parsedIds=targetIds.Select(id=>Guid.TryParse(id,out var parsed)?parsed:(Guid?)null).Where(id=>id.HasValue).Select(id=>id!.Value).ToList();
        var workspaces=await db.EmployerWorkspaces.AsNoTracking().Where(workspace=>parsedIds.Contains(workspace.Id)).ToListAsync();
        var requests=await db.EmployerAccessRequests.AsNoTracking().Where(request=>parsedIds.Contains(request.Id)).ToListAsync();
        var workspaceIds=workspaces.Select(workspace=>workspace.Id).Concat(requests.Select(request=>request.WorkspaceId)).Distinct().ToList();
        var loadedWorkspaceIds=workspaces.Select(item=>item.Id).ToList();
        workspaces.AddRange(await db.EmployerWorkspaces.AsNoTracking().Where(workspace=>workspaceIds.Contains(workspace.Id)&&!loadedWorkspaceIds.Contains(workspace.Id)).ToListAsync());
        var ownerIds=workspaces.Select(workspace=>workspace.OwnerId).Distinct().ToList();
        var owners=await db.Users.AsNoTracking().Where(user=>ownerIds.Contains(user.Id)).ToDictionaryAsync(user=>user.Id);
        return entries.Select(entry=>{
            var targetUser=targetUsers.FirstOrDefault(user=>user.Id==entry.EntityId);
            var workspace=workspaces.FirstOrDefault(item=>Guid.TryParse(entry.EntityId,out var id)&&item.Id==id);
            var request=requests.FirstOrDefault(item=>Guid.TryParse(entry.EntityId,out var id)&&item.Id==id);
            if(request is not null)workspace=workspaces.FirstOrDefault(item=>item.Id==request.WorkspaceId);
            var account=workspace is not null?WorkspaceName(workspace,owners):targetUser is not null?UserName(targetUser):PlatformTarget(entry);
            var info=workspace is not null&&owners.TryGetValue(workspace.OwnerId,out var owner)?$"Owner: {UserName(owner)}":targetUser is not null?$"{targetUser.Email} · {targetUser.AccountType}":request is not null?$"{request.Feature} request":entry.EntityType;
            return new AdminActivitySummary(entry.CreatedUtc,ReadableAction(entry.Action),account,info,ReadableDetails(entry,request));
        }).ToList();
    }

    record UserAccessSnapshot(string FullName,string Role,string Status,bool CanCreatePositions,bool CanCreateClients,bool CanViewTalentPool,bool CanExportReports,DateTime? TalentPoolPaidUntilUtc);
    record WorkspaceAccessSnapshot(int Seats,int PositionSlots,bool TeamTalentPoolEnabled,bool MasterTalentPoolEnabled,bool AgencyEnabled,bool EmailTemplatesEnabled=false);
    static string UserName(AtsUser user)=>string.IsNullOrWhiteSpace(user.FullName)?user.UserName??"Account":user.FullName;
    static string WorkspaceName(EmployerWorkspace workspace,Dictionary<string,AtsUser> owners)=>$"{workspace.CompanyName} · {(owners.TryGetValue(workspace.OwnerId,out var owner)?UserName(owner):"Owner unavailable")}";
    static string PlatformTarget(AuditEntry entry)=>entry.EntityId is "settings" or "decision-rules"?"Platform settings":entry.EntityType switch{"Application"=>"Candidate application","Candidate"=>"Candidate profile",_=>"Platform activity"};
    static string ReadableAction(string action)=>action switch{"Employer entitlement updated"=>"Workspace Access Updated","Employer request responded"=>"Subscription Request Reviewed","User access updated"=>"Account Access Updated","User created"=>"Account Created","Password reset"=>"Password Reset","Decision rules updated"=>"Decision Rules Updated",_=>action};
    static string ReadableDetails(AuditEntry entry,EmployerAccessRequest? request)
    {
        if(entry.Action is "Sub-Admin created" or "Sub-Admin access updated"){
            var parts=entry.Detail.Split(';',2,StringSplitOptions.TrimEntries);
            var permissions=parts.Length>1?string.Join(" · ",parts[1].Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(key=>AdminPermissionLabels.GetValueOrDefault(key,key))):"";
            return parts[0]+(permissions.Length>0?" · "+permissions:"");
        }
        if(entry.Action=="Employer entitlement updated")return DescribeWorkspaceAccess(entry);
        if(entry.Action=="User access updated")return DescribeUserAccess(entry);
        if(entry.Action=="Employer request responded"){
            if(request is not null)return $"{request.Feature}: {StatusText(request.Status)} · {request.AdminResponse}";
            var parts=entry.Detail.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            return parts.Length>0?$"Request {StatusText(parts[0])}: {string.Join(" · ",parts.Skip(1))}":"Subscription request reviewed.";
        }
        if(entry.Action=="User created")return $"Account created with {entry.Detail} access.";
        if(entry.Action=="Password reset")return "The account password was reset by an Admin.";
        if(entry.Action=="Decision rules updated")return "The platform’s assessment decision rules were updated.";
        return string.IsNullOrWhiteSpace(entry.Detail)?"No additional details were recorded.":entry.Detail;
    }
    static string DescribeWorkspaceAccess(AuditEntry entry)
    {
        var before=ReadSnapshot(entry.PreviousJson);var after=ReadSnapshot(entry.NewJson);
        if(before is not null&&after is not null){
            var changes=new List<string>();AddChange(changes,"Recruiter seats",before.Seats,after.Seats);AddChange(changes,"Position slots",before.PositionSlots,after.PositionSlots);AddChange(changes,"Team Talent Pool",before.TeamTalentPoolEnabled,after.TeamTalentPoolEnabled);AddChange(changes,"Master Talent Pool",before.MasterTalentPoolEnabled,after.MasterTalentPoolEnabled);AddChange(changes,"Agency access",before.AgencyEnabled,after.AgencyEnabled);AddChange(changes,"Email Templates",before.EmailTemplatesEnabled,after.EmailTemplatesEnabled);
            return changes.Count>0?string.Join(" · ",changes):$"Access saved: {AccessState(after)}";
        }
        var values=entry.Detail.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(part=>part.Split('=',2,StringSplitOptions.TrimEntries)).Where(pair=>pair.Length==2).ToDictionary(pair=>pair[0],pair=>pair[1],StringComparer.OrdinalIgnoreCase);
        string Value(string key,string label)=>values.TryGetValue(key,out var value)?$"{label}: {value switch{"True"=>"enabled","False"=>"disabled",_=>value}}":"";
        var legacy=new[]{Value("Seats","Recruiter seats"),Value("slots","Position slots"),Value("team talent","Team Talent Pool"),Value("master talent","Master Talent Pool"),Value("agency","Agency access")}.Where(value=>value.Length>0).ToList();
        return legacy.Count>0?$"Current access: {string.Join(" · ",legacy)}.":"Workspace access settings were updated.";
    }
    static WorkspaceAccessSnapshot? ReadSnapshot(string json){try{return string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<WorkspaceAccessSnapshot>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});}catch(JsonException){return null;}}
    static UserAccessSnapshot? ReadUserSnapshot(string json){try{return string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<UserAccessSnapshot>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});}catch(JsonException){return null;}}
    static string DescribeUserAccess(AuditEntry entry)
    {
        var before=ReadUserSnapshot(entry.PreviousJson);var after=ReadUserSnapshot(entry.NewJson);
        if(before is not null&&after is not null){
            var changes=new List<string>();AddChange(changes,"Name",before.FullName,after.FullName);AddChange(changes,"Role",before.Role,after.Role);AddChange(changes,"Account status",before.Status,after.Status);AddChange(changes,"Create positions",before.CanCreatePositions,after.CanCreatePositions);AddChange(changes,"Create clients",before.CanCreateClients,after.CanCreateClients);AddChange(changes,"Talent Pool access",before.CanViewTalentPool,after.CanViewTalentPool);AddChange(changes,"Export reports",before.CanExportReports,after.CanExportReports);AddChange(changes,"Talent Pool paid-through",before.TalentPoolPaidUntilUtc?.ToString("dd MMM yyyy")??"Not set",after.TalentPoolPaidUntilUtc?.ToString("dd MMM yyyy")??"Not set");
            return changes.Count>0?string.Join(" · ",changes):"No account access values changed.";
        }
        var raw=entry.Detail.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        return raw.Length>1?$"Role: {raw[0]} · Account status: {raw[1]} · {string.Join(" · ",raw.Skip(2).Select(HumanizeSetting))}":"Account access settings were updated.";
    }
    static string HumanizeSetting(string value){var pair=value.Split('=',2,StringSplitOptions.TrimEntries);if(pair.Length!=2)return value;var label=pair[0] switch{"create positions"=>"Create positions","create clients"=>"Create clients","reports"=>"Export reports","talent paid until"=>"Talent Pool paid-through",_=>pair[0]};var state=pair[1] switch{"True"=>"enabled","False"=>"disabled",""=>"not set",_=>pair[1]};return $"{label}: {state}";}
    static string AccessState(WorkspaceAccessSnapshot value)=>$"{value.Seats} recruiter seats · {value.PositionSlots} position slots · Team Talent Pool {(value.TeamTalentPoolEnabled?"enabled":"disabled")} · Master Talent Pool {(value.MasterTalentPoolEnabled?"enabled":"disabled")} · Agency access {(value.AgencyEnabled?"enabled":"disabled")}";
    static string StatusText(string value)=>value switch{"Granted"=>"granted","Rejected"=>"rejected","Under Review"=>"under review","Needs Information"=>"needs information",_=>value.ToLowerInvariant()};
    static void AddChange<T>(List<string> changes,string label,T before,T after){if(!EqualityComparer<T>.Default.Equals(before,after))changes.Add($"{label}: {DisplayValue(before)} → {DisplayValue(after)}");}
    static string DisplayValue<T>(T value)=>value switch{bool flag=>flag?"on":"off",DateTime date=>date.ToString("dd MMM yyyy"),_=>value?.ToString()??"not set"};
}
