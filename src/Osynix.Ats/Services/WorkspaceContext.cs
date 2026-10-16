using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public record ActiveWorkspace(Guid? Id,bool Owner,WorkspaceMembership? Membership=null,bool Legacy=false);
public record WorkspaceChoice(Guid Id,string Name,string Role,bool Owner);
public static class WorkspaceContext {
 public const string Claim="Osynix.Workspace";
 public static async Task<bool> OwnerActive(AtsDbContext db,string id)=>await db.Users.AnyAsync(u=>u.Id==id&&u.Status=="Active"&&u.EmailConfirmed&&((u.AccountType==AccountTypes.Freelance&&db.UserRoles.Any(ur=>ur.UserId==id&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Recruiter")))||db.UserRoles.Any(ur=>ur.UserId==id&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Employer")))) ;
 public static async Task<ActiveWorkspace> Resolve(AtsDbContext db,ClaimsPrincipal principal) {
  if(principal.IsInRole("Admin"))return new(null,true);
  var uid=principal.FindFirstValue(ClaimTypes.NameIdentifier);var account=await db.Users.FindAsync(uid)??throw new UnauthorizedAccessException("Account Unavailable.");
  if(account.Status!="Active"||(account.AccountType!=AccountTypes.Legacy&&!account.EmailConfirmed))throw new UnauthorizedAccessException("Account Inactive.");
  Guid? selected=Guid.TryParse(principal.FindFirstValue(Claim),out var parsed)?parsed:null;
  var owner=await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.OwnerId==uid);
  var hasOwn=principal.IsInRole("Employer")||account.AccountType==AccountTypes.Freelance;
  if(selected is null&&hasOwn){owner??=await EmployerService.EnsureWorkspace(db,account);selected=owner.Id;}
  if(selected is Guid id){
   var w=await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.Id==id)??throw new UnauthorizedAccessException("Workspace Unavailable.");
   if(!await OwnerActive(db,w.OwnerId))throw new UnauthorizedAccessException("Workspace Inactive. Choose Another Workspace.");
   if(w.OwnerId==uid)return new(id,true);
   var member=await db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.WorkspaceId==id&&m.UserId==uid&&m.Status=="Active");
   if(member is null)throw new UnauthorizedAccessException("Team Access Is Inactive Or Was Revoked. Choose Another Workspace.");
   return new(id,false,member);
  }
  if(account.AccountType==AccountTypes.Legacy)return new(account.EmployerWorkspaceId,false,null,true);
  var first=await db.WorkspaceMemberships.AsNoTracking().Where(m=>m.UserId==uid&&m.Status=="Active").OrderBy(m=>m.CreatedUtc).ToListAsync();
  foreach(var member in first){var w=await db.EmployerWorkspaces.FindAsync(member.WorkspaceId);if(w is not null&&await OwnerActive(db,w.OwnerId))return new(w.Id,false,member);}
  if(account.AccountType==AccountTypes.Employee)throw new UnauthorizedAccessException("No Active Team Membership. Ask Your Employer To Restore Your Workspace Access.");
  // Compatibility for imported records and legacy service integrations; new accounts never use this path.
  return new(account.EmployerWorkspaceId,false,null,true);
 }
 public static ClaimsPrincipal Effective(ClaimsPrincipal principal,ActiveWorkspace active){
  if(principal.IsInRole("Admin")||active.Legacy)return principal;
  var identity=new ClaimsIdentity(principal.Identity as ClaimsIdentity);
  foreach(var role in identity.FindAll(ClaimTypes.Role).ToArray())identity.RemoveClaim(role);
  identity.AddClaim(new(ClaimTypes.Role,active.Owner?"Employer":"Recruiter"));
  return new(identity);
 }
 public static async Task<List<WorkspaceChoice>> Choices(AtsDbContext db,ClaimsPrincipal principal){
  var uid=principal.FindFirstValue(ClaimTypes.NameIdentifier);var account=await db.Users.FindAsync(uid);if(account is null||account.Status!="Active"||principal.IsInRole("Admin"))return [];
  var result=new List<WorkspaceChoice>();
  if(principal.IsInRole("Employer")||account.AccountType==AccountTypes.Freelance){var own=await EmployerService.EnsureWorkspace(db,account);result.Add(new(own.Id,own.CompanyName,own.Kind==AccountTypes.Freelance?AccountTypes.Freelance:"Employer",true));}
  var members=await db.WorkspaceMemberships.AsNoTracking().Where(m=>m.UserId==uid&&m.Status=="Active").ToListAsync();foreach(var m in members){var w=await db.EmployerWorkspaces.FindAsync(m.WorkspaceId);if(w is not null&&await OwnerActive(db,w.OwnerId))result.Add(new(w.Id,w.CompanyName,"Team Member",false));}
  return result;
 }
}
