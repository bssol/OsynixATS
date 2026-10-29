using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[Authorize(Roles="Employer")]
public class ProfileModeModel(IDbContextFactory<AtsDbContext> factory,SignInManager<AtsUser> signIn):PageModel {
 [BindProperty] public string Mode{get;set;}="Employer";
 public IActionResult OnGet()=>LocalRedirect("/");
 public async Task<IActionResult> OnPostAsync(){if(Mode is not("Employer" or "Agency"))return BadRequest();await using var db=await factory.CreateDbContextAsync();var active=await WorkspaceContext.Resolve(db,User);var account=await db.Users.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));if(!active.Owner||account?.AccountType!=AccountTypes.Employer||!account.EmailConfirmed||account.Status!="Active")return Forbid();var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==active.Id);if(w.Kind!=AccountTypes.Employer)return Forbid();if(Mode=="Agency"&&!w.AgencyEnabled)return LocalRedirect("/employer/subscriptions");await signIn.SignInWithClaimsAsync(account,false,[new Claim(WorkspaceContext.Claim,w.Id.ToString()),new Claim(ProfileModes.Claim,Mode)]);return LocalRedirect("/");}
}
