using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osynix.Ats.Data;
namespace Osynix.Ats.Pages.Account;
[Authorize]
public class LinkGoogleModel(SignInManager<AtsUser> signIn,UserManager<AtsUser> users,IAuthenticationSchemeProvider schemes):PageModel {
 public bool Available{get;set;} public bool Complete{get;set;}
 public async Task OnGetAsync(){Available=await schemes.GetSchemeAsync("Google") is not null;}
 public async Task<IActionResult> OnPostAsync(){Available=await schemes.GetSchemeAsync("Google") is not null;if(!Available)return Page();return Challenge(signIn.ConfigureExternalAuthenticationProperties("Google",Url.Page("LinkGoogle","Callback")!,users.GetUserId(User)),"Google");}
 public async Task<IActionResult> OnGetCallbackAsync(){
  Available=await schemes.GetSchemeAsync("Google") is not null;
  var account=await users.GetUserAsync(User);var info=await signIn.GetExternalLoginInfoAsync(account?.Id);
  if(account is null||account.Status!="Active"||info is null||!string.Equals(info.Principal.FindFirstValue("email_verified"),"true",StringComparison.OrdinalIgnoreCase)||!string.Equals(account.Email,info.Principal.FindFirstValue(ClaimTypes.Email),StringComparison.OrdinalIgnoreCase)){ModelState.AddModelError("","Connect the verified Google account matching your Osynix email.");return Page();}
  var linked=await users.FindByLoginAsync(info.LoginProvider,info.ProviderKey);
  Complete=linked?.Id==account.Id||(await users.AddLoginAsync(account,info)).Succeeded;if(!Complete)ModelState.AddModelError("","This Google account could not be linked.");return Page();
 }
}
