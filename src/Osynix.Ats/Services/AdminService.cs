using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public record WorkspaceUser(string Id,string Username,string Email,string FullName,string Role,string Status,bool CanCreatePositions,bool CanViewTalentPool,bool CanExportReports,bool HasPassword,bool CanCreateClients=false,DateTime? TalentPoolPaidUntilUtc=null,Guid? EmployerWorkspaceId=null,string AccountType="Legacy");
public class AdminService(Access access,IServiceScopeFactory scopes,IDbContextFactory<AtsDbContext> factory)
{
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
        Check(await manager.UpdateSecurityStampAsync(u));AtsService.Audit(db,admin.Identity!.Name!,"User access updated",u.Id,$"{input.Role}; {input.Status}; create positions={u.CanCreatePositions}; create clients={u.CanCreateClients}; talent paid until={u.TalentPoolPaidUntilUtc:O}; reports={u.CanExportReports}");await db.SaveChangesAsync();await tx.CommitAsync();
    }
    public async Task ResetPassword(string id,string password)
    {
        var admin=await access.Require(true);using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var u=await manager.FindByIdAsync(id)??throw new InvalidOperationException("User not found.");
        Check(await manager.ResetPasswordAsync(u,await manager.GeneratePasswordResetTokenAsync(u),password));await manager.ResetAccessFailedCountAsync(u);await manager.SetLockoutEndDateAsync(u,null);
        await using var db=await factory.CreateDbContextAsync();AtsService.Audit(db,admin.Identity!.Name!,"Password reset",u.Id,"Administrator reset the account password.");await db.SaveChangesAsync();
    }
    public async Task<List<AuditEntry>> Audit()
    {await access.Require(true);await using var db=await factory.CreateDbContextAsync();return await db.Audit.AsNoTracking().OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync();}
}
