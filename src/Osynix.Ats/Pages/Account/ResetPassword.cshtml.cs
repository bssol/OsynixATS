using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Osynix.Ats.Data;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class ResetPasswordModel(UserManager<AtsUser> users):PageModel {
 [BindProperty(SupportsGet=true),Required,EmailAddress]public string Email{get;set;}="";
 [BindProperty(SupportsGet=true),Required]public string Token{get;set;}="";
 [BindProperty,Required,MinLength(6)]public string Password{get;set;}="";
 [BindProperty,Compare(nameof(Password))]public string ConfirmPassword{get;set;}="";
 public bool Complete{get;set;}
 public async Task<IActionResult> OnPostAsync(){
  if(!ModelState.IsValid)return Page();var user=await users.FindByEmailAsync(Email);
  string decoded;try{decoded=Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Token));}catch{decoded="";}
  if(user is null||!user.EmailConfirmed||!(await users.ResetPasswordAsync(user,decoded,Password)).Succeeded){ModelState.AddModelError("","This reset link is invalid or expired. Request a new link.");return Page();}
  await users.UpdateSecurityStampAsync(user);Complete=true;return Page();
 }
}
