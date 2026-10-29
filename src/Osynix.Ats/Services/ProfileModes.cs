using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class ProfileModes {
 public const string Claim="Osynix.ProfileMode";
 public static async Task<bool?> Selected(AtsDbContext db,ClaimsPrincipal user,ActiveWorkspace active){if(!active.Owner||user.IsInRole("Admin")||active.Id is null)return null;var account=await db.Users.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));if(account?.AccountType!=AccountTypes.Employer)return null;var w=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==active.Id);if(w.Kind!=AccountTypes.Employer)return null;return w.AgencyEnabled&&user.FindFirstValue(Claim)=="Agency";}
}
