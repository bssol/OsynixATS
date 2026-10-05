using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;

namespace Osynix.Ats.Services;

public static class EndpointAccess
{
    public static async Task<bool> Allowed(AtsDbContext db,ClaimsPrincipal principal,string? permission=null)
    {
        var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);if(id is null) return false;
        var account=await db.Users.FindAsync(id);if(account is null||account.Status!="Active") return false;
        var roles=await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ur.UserId==id select role.Name!).ToListAsync();
        if(!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order())) return false;
        if(roles.Contains("Admin")) return true;
        return roles.Contains("Recruiter")&&(permission switch {null=>true,"Reports.Export"=>account.CanExportReports,"Talent.View"=>account.CanViewTalentPool,"Positions.Create"=>account.CanCreatePositions,_=>false});
    }
}
