using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class GoogleModel(SignInManager<AtsUser> signIn,UserManager<AtsUser> users,IAuthenticationSchemeProvider schemes,EmployerTeamService teams):PageModel {
 [BindProperty(SupportsGet=true)]public string? ReturnUrl{get;set;}
 [BindProperty]public string FullName{get;set;}="";
 [BindProperty]public string Role{get;set;}="Recruiter";
 public bool Registering{get;set;} public bool Available{get;set;}
 public async Task OnGetAsync(string? error=null){Available=await schemes.GetSchemeAsync("Google") is not null;if(error is not null)ModelState.AddModelError("","Google sign-in could not complete. Please try again.");}
 public async Task<IActionResult> OnPostStartAsync(){
  if(await schemes.GetSchemeAsync("Google") is null){Available=false;return Page();}
  return Challenge(signIn.ConfigureExternalAuthenticationProperties("Google",Url.Page("Google","Callback",new{returnUrl=Url.IsLocalUrl(ReturnUrl)?ReturnUrl:null})!),"Google");
 }
 static bool Verified(ExternalLoginInfo info)=>string.Equals(info.Principal.FindFirstValue("email_verified"),"true",StringComparison.OrdinalIgnoreCase);
 public async Task<IActionResult> OnGetCallbackAsync(){
  Available=await schemes.GetSchemeAsync("Google") is not null;
  var info=await signIn.GetExternalLoginInfoAsync();
  if(info is null||!Verified(info)){ModelState.AddModelError("","Google could not verify your account.");return Page();}
  var account=await users.FindByLoginAsync(info.LoginProvider,info.ProviderKey);
  if(account is not null){
   if(account.Status!="Active"){ModelState.AddModelError("","This account is not active.");return Page();}
   var result=await signIn.ExternalLoginSignInAsync(info.LoginProvider,info.ProviderKey,false,false);
   if(result.Succeeded)return LocalRedirect(Url.IsLocalUrl(ReturnUrl)?ReturnUrl!:AccountRegistration.Home(await users.GetRolesAsync(account)));
   ModelState.AddModelError("","Sign-in could not complete.");return Page();
  }
  Registering=true;FullName=info.Principal.FindFirstValue(ClaimTypes.Name)??"";return Page();
 }
 public async Task<IActionResult> OnPostCompleteAsync(){
  Available=await schemes.GetSchemeAsync("Google") is not null;Registering=true;var info=await signIn.GetExternalLoginInfoAsync();
  var email=info?.Principal.FindFirstValue(ClaimTypes.Email);
  if(info is null||!Verified(info)||string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(FullName)||FullName.Length>100||!AccountRegistration.Roles.Contains(Role)){ModelState.AddModelError("","Choose a role and enter your name. Start Google sign-in again if the session expired.");return Page();}
  if(await users.FindByEmailAsync(email) is not null){ModelState.AddModelError("","Sign in with your password first, then connect Google from My Profile.");return Page();}
  var account=AccountRegistration.NewUser(email,FullName,Role);
  if(Role=="Recruiter"&&ReturnUrl?.StartsWith("/Account/TeamInvitation?",StringComparison.Ordinal)==true){var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://local"+ReturnUrl).Query);var invitation=await teams.Read(query["token"]);if(invitation?.Status=="Pending"&&string.Equals(invitation.Email,email,StringComparison.OrdinalIgnoreCase))account.AccountType=Osynix.Ats.Domain.AccountTypes.Employee;}account.EmailConfirmed=true;account.Status="Active";
  var created=await users.CreateAsync(account);
  if(!created.Succeeded){ModelState.AddModelError("","Unable to create this account.");return Page();}
  var assigned=await users.AddToRoleAsync(account,Role);var linked=await users.AddLoginAsync(account,info);
  if(!assigned.Succeeded||!linked.Succeeded){await users.DeleteAsync(account);ModelState.AddModelError("","Unable to complete registration.");return Page();}
  if(Role=="Recruiter")await users.AddClaimAsync(account,new Claim("WorkspaceScope","Assigned"));
  await signIn.SignInAsync(account,false);return LocalRedirect(Url.IsLocalUrl(ReturnUrl)?ReturnUrl!:AccountRegistration.Home(await users.GetRolesAsync(account)));
 }
}
