using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[Authorize]
public class WorkspacesModel(IDbContextFactory<AtsDbContext> factory,SignInManager<AtsUser> signIn):PageModel {
 public List<WorkspaceChoice> Workspaces{get;set;}=[];
 [BindProperty]public string? ProfileMode{get;set;}
 [BindProperty]public string? ReturnUrl{get;set;}
 [BindProperty]public Guid WorkspaceId{get;set;}
 public async Task OnGetAsync(){await using var db=await factory.CreateDbContextAsync();Workspaces=await WorkspaceContext.Choices(db,User);}
 public async Task<IActionResult> OnPostAsync(){await using var db=await factory.CreateDbContextAsync();Workspaces=await WorkspaceContext.Choices(db,User);if(!Workspaces.Any(w=>w.Id==WorkspaceId))return Forbid();var account=await db.Users.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));if(account is null||!account.EmailConfirmed)return Forbid();var claims=new List<Claim>{new(WorkspaceContext.Claim,WorkspaceId.ToString())};var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==WorkspaceId);if(w.OwnerId==account.Id&&account.AccountType==Osynix.Ats.Domain.AccountTypes.Employer&&ProfileMode is "Employer" or "Agency"){if(ProfileMode=="Agency"&&!w.AgencyEnabled)return Forbid();claims.Add(new(ProfileModes.Claim,ProfileMode));}await signIn.SignInWithClaimsAsync(account,false,claims);return LocalRedirect(Url.IsLocalUrl(ReturnUrl)?ReturnUrl!:"/");}
}
