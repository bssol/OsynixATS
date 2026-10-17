using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[Authorize,EnableRateLimiting("auth")]
public class PasswordModel(UserManager<AtsUser> users,SignInManager<AtsUser> signIn):PageModel {
 [BindProperty]public string? CurrentPassword{get;set;}="";
 [BindProperty,Required,DataType(DataType.Password)]public string NewPassword{get;set;}="";
 [BindProperty,Required,Compare(nameof(NewPassword),ErrorMessage="The passwords do not match.")]public string ConfirmPassword{get;set;}="";
 public bool HasPassword{get;set;} public bool Complete{get;set;}
 public string BackUrl{get;set;}="/profile";
 async Task<AtsUser?> Load(){
  var account=await users.GetUserAsync(User);
  if(account is not null){HasPassword=await users.HasPasswordAsync(account);BackUrl=AccountRegistration.Home(await users.GetRolesAsync(account)) is "/" or "/settings"?"/profile":"/Account/Portal";}
  return account;
 }
 public async Task<IActionResult> OnGetAsync(){var account=await Load();return account is null||account.Status!="Active"?Challenge():Page();}
 public async Task<IActionResult> OnPostAsync(){
  var account=await Load();if(account is null||account.Status!="Active")return Challenge();
  if(HasPassword&&string.IsNullOrEmpty(CurrentPassword))ModelState.AddModelError(nameof(CurrentPassword),"Enter your current ATS password.");
  if(!ModelState.IsValid)return Page();
  var result=HasPassword?await users.ChangePasswordAsync(account,CurrentPassword!,NewPassword):await users.AddPasswordAsync(account,NewPassword);
  if(!result.Succeeded){foreach(var error in result.Errors)ModelState.AddModelError("",error.Description);return Page();}
  await signIn.RefreshSignInAsync(account);HasPassword=true;Complete=true;
  CurrentPassword=NewPassword=ConfirmPassword="";ModelState.Clear();return Page();
 }
}
