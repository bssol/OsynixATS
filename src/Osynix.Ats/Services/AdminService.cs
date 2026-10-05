using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public class AdminService(Access access,IServiceScopeFactory scopes,IDbContextFactory<AtsDbContext> factory)
{
    public async Task<List<string>> Users()
    {
        await access.Require(true); using var scope=scopes.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var result=new List<string>();
        foreach(var user in await users.Users.ToListAsync()) result.Add($"{user.Email ?? user.UserName} — {string.Join(", ",await users.GetRolesAsync(user))}");return result;
    }
    public async Task CreateUser(string email,string password,string role)
    {
        var admin=await access.Require(true); if(role is not ("Admin" or "Recruiter")) throw new InvalidOperationException("Invalid role.");
        if(!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)) throw new InvalidOperationException("Enter a valid email.");
        using var scope=scopes.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
        var user=new AtsUser{Email=email.Trim(),UserName=email.Trim(),EmailConfirmed=true};
        var result=await users.CreateAsync(user,password);if(!result.Succeeded)throw new InvalidOperationException(string.Join("; ",result.Errors.Select(e=>e.Description)));
        var assigned=await users.AddToRoleAsync(user,role);if(!assigned.Succeeded){await users.DeleteAsync(user);throw new InvalidOperationException("Role assignment failed; user was not created.");}
        await using var db=await factory.CreateDbContextAsync();AtsService.Audit(db,admin.Identity!.Name!,"User created",user.Id,role);await db.SaveChangesAsync();
    }
    public async Task<List<AuditEntry>> Audit()
    {await access.Require(true);await using var db=await factory.CreateDbContextAsync();return await db.Audit.AsNoTracking().OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync();}
}
