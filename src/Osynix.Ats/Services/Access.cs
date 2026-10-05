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
        if (user.Identity?.IsAuthenticated != true || !(user.IsInRole("Admin") || (!admin && user.IsInRole("Recruiter"))))
            throw new UnauthorizedAccessException("You do not have permission for this action.");
        if(factory is not null) {
            await using var db=await factory.CreateDbContextAsync();
            var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));
            if(account is null || account.Status!="Active") throw new UnauthorizedAccessException("This account is not active.");
            var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==account.Id select role.Name!).ToListAsync();
            if(!roles.Order().SequenceEqual(user.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order())) throw new UnauthorizedAccessException("Your permissions changed. Sign in again.");
        }
        return user;
    }
    public async Task<string> UserId() => (await Require()).FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task RequirePermission(string permission)
    {
        var user=await Require();
        if(user.IsInRole("Admin") || factory is null) return;
        await using var db=await factory.CreateDbContextAsync();
        var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));
        var allowed=account is not null && (permission switch { "Positions.Create"=>account.CanCreatePositions,"Talent.View"=>account.CanViewTalentPool,"Reports.Export"=>account.CanExportReports,_=>false });
        if(!allowed) throw new UnauthorizedAccessException("Your account does not have permission for this action.");
    }
}
