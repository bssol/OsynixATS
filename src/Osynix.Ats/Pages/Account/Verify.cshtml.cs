using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class VerifyModel(UserManager<AtsUser> users,AccountRegistration registration):PageModel {
 [BindProperty(SupportsGet=true),EmailAddress,Required]public string Email{get;set;}="";
 [BindProperty(SupportsGet=true)]public string? Invite{get;set;}
 [BindProperty]public string Code{get;set;}="";
 public bool Complete{get;set;} public string Notice{get;set;}="";
 public async Task<IActionResult> OnPostAsync(){
  if(!ModelState.IsValid||Code.Length!=6||!Code.All(char.IsAsciiDigit)){ModelState.AddModelError("","Enter the six-digit code.");return Page();}
  var user=await users.FindByEmailAsync(Email);
  if(user is not null&&await registration.Verify(user,Code)){Complete=true;return Page();}
  ModelState.AddModelError("","The code is invalid or expired. Request a new code if needed.");return Page();
 }
 public async Task<IActionResult> OnPostResendAsync(){
  if(!ModelState.IsValid)return Page();var user=await users.FindByEmailAsync(Email);
  try{if(user is not null&&!user.EmailConfirmed)await registration.SendCode(user);Notice="If your account needs verification, a new code has been sent.";}
  catch(InvalidOperationException e){ModelState.AddModelError("",e.Message);}return Page();
 }
}
