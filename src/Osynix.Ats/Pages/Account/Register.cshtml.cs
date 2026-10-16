using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class RegisterModel(AccountRegistration registration,EmployerTeamService teams):PageModel {
 [BindProperty,Required,StringLength(100)]public string FullName{get;set;}="";
 [BindProperty,Required,EmailAddress]public string Email{get;set;}="";
 [BindProperty,Required]public string Role{get;set;}="Recruiter";
 [BindProperty,Required,MinLength(6)]public string Password{get;set;}="";
 [BindProperty,Compare(nameof(Password))]public string ConfirmPassword{get;set;}="";
 [BindProperty(SupportsGet=true)]public string? Invite{get;set;}
 public async Task OnGetAsync(string? role=null){if(AccountRegistration.Roles.Contains(role))Role=role!;if(Invite is {Length:>0}){var invitation=await teams.Read(Invite);if(invitation?.Status=="Pending"){Role="Recruiter";Email=invitation.Email;FullName=invitation.Name;}}}
 public async Task<IActionResult> OnPostAsync(){
  if(!ModelState.IsValid)return Page();
  if(Invite is {Length:>0}){var invitation=await teams.Read(Invite);if(invitation?.Status!="Pending"||Role!="Recruiter"||!string.Equals(Email.Trim(),invitation.Email,StringComparison.OrdinalIgnoreCase)){ModelState.AddModelError("","Use The Invited Email Address And Recruiter Role.");return Page();}}
  try{await registration.Register(Email.Trim(),FullName,Password,Role,teamOnly:Invite is {Length:64});return RedirectToPage("Verify",new{email=Email.Trim(),invite=Invite});}
  catch(InvalidOperationException e){ModelState.AddModelError("",e.Message);return Page();}
 }
 public IActionResult OnPostGoogle()=>RedirectToPage("Google",new{returnUrl=Invite is {Length:64}?"/Account/TeamInvitation?token="+Uri.EscapeDataString(Invite):null});
}
