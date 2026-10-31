using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Integrations;
[Authorize(Roles="Admin,Employer,Recruiter")]
[ValidateAntiForgeryToken]
public class GoogleMeetModel(GoogleMeetService meet,AtsDbContext db):PageModel {
 const string Settings="/settings/integrations?provider=google-meet";
 string Owner=>User.FindFirstValue(ClaimTypes.NameIdentifier)!;
 public Task<IActionResult> OnGetAsync(){Response.Headers.CacheControl="no-store";return Task.FromResult<IActionResult>(Redirect(Settings));}
 public async Task<IActionResult> OnPostAsync(){
  Response.Headers.CacheControl="no-store";
  IntegrationEntitlement permission;try{permission=await IntegrationEntitlements.Require(db,User);}catch(UnauthorizedAccessException){return Forbid();}
  if(!meet.Configured)return Redirect(Settings);
  var props=new AuthenticationProperties{RedirectUri=Settings};props.Items["owner"]=Owner;props.Items["integration-workspace"]=IntegrationEntitlements.ContextKey(permission);
  return Challenge(props,GoogleMeetService.Scheme);
 }
 public async Task<IActionResult> OnPostDisconnectAsync(){
  try{await IntegrationEntitlements.Require(db,User);}catch(UnauthorizedAccessException){return Forbid();}
  try{await meet.Disconnect(Owner);}catch(InvalidOperationException){return Redirect(Settings+"&error=active-interviews");}
  return Redirect(Settings);
 }
}
