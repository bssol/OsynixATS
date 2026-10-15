using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class ForgotPasswordModel(UserManager<AtsUser> users,IAccountEmail mail,IConfiguration config):PageModel {
 [BindProperty,Required,EmailAddress]public string Email{get;set;}="";public bool Sent{get;set;}
 public async Task<IActionResult> OnPostAsync(){
  if(!ModelState.IsValid)return Page();
  if(!mail.Configured){ModelState.AddModelError("","Password recovery is currently unavailable. Please try again later.");return Page();}
  var user=await users.FindByEmailAsync(Email.Trim());
  try{if(user is not null&&user.EmailConfirmed&&user.Status=="Active"){
   var token=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
   var origin=(config["Auth:PublicBaseUrl"]??"http://localhost:5080").TrimEnd('/');
   await mail.Send(user.Email!,"Reset Your Osynix Password",$"Use this secure link to choose a new password:\n{origin}/Account/ResetPassword?email={Uri.EscapeDataString(user.Email!)}&token={token}\n\nThe link expires in one hour.");
  }Sent=true;}catch{ModelState.AddModelError("","We could not send reset instructions. Please try again later.");}return Page();
 }
}
