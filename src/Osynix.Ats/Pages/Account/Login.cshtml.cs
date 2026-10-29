using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
using Microsoft.AspNetCore.RateLimiting;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class LoginModel(SignInManager<AtsUser> signIn, UserManager<AtsUser> users) : PageModel
{
    [BindProperty,Required] public string Email{get;set;}="";
    [BindProperty,Required] public string Password{get;set;}="";
    public async Task<IActionResult> OnPostAsync(string? returnUrl=null)
    {
        if(!ModelState.IsValid)return Page();
        var account=await users.FindByEmailAsync(Email.Trim()) ?? await users.FindByNameAsync(Email.Trim());
        if(account is null || account.Status!="Active") { ModelState.AddModelError("","Sign-in failed. Check your credentials or try again later."); return Page(); }
        if(!account.EmailConfirmed && (await users.GetClaimsAsync(account)).Any(x=>x.Type=="WorkspaceScope")){ModelState.AddModelError("","Verify your email before signing in.");return Page();}
        if(!await users.HasPasswordAsync(account)){ModelState.AddModelError("","This account uses Google sign-in. Continue with Google, then set an ATS password in My Profile to use email sign-in.");return Page();}
        var result=await signIn.PasswordSignInAsync(account,Password,false,true);
        if(result.Succeeded) return LocalRedirect(Url.IsLocalUrl(returnUrl)?returnUrl!:AccountRegistration.Home(await users.GetRolesAsync(account)));
        ModelState.AddModelError("","Sign-in failed. Check your credentials or try again later.");return Page();
    }
    public IActionResult OnPostGoogle(string? returnUrl=null)=>RedirectToPage("Google",new{returnUrl=Url.IsLocalUrl(returnUrl)?returnUrl:null});
}
