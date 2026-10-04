using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osynix.Ats.Data;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous]
public class LoginModel(SignInManager<AtsUser> signIn) : PageModel
{
    [BindProperty,Required,EmailAddress] public string Email{get;set;}="";
    [BindProperty,Required] public string Password{get;set;}="";
    public async Task<IActionResult> OnPostAsync(string? returnUrl=null)
    {
        if(!ModelState.IsValid)return Page();
        var result=await signIn.PasswordSignInAsync(Email,Password,false,true);
        if(result.Succeeded) return LocalRedirect(Url.IsLocalUrl(returnUrl)?returnUrl!:"/");
        ModelState.AddModelError("","Sign-in failed. Check your credentials or try again later.");return Page();
    }
}
