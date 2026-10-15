using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osynix.Ats.Data;
namespace Osynix.Ats.Pages.Account;
[Authorize]
public class PortalModel(UserManager<AtsUser> users):PageModel {
 public string Name{get;set;}="";public string Role{get;set;}="";
 public async Task<IActionResult> OnGetAsync(){var account=await users.GetUserAsync(User);if(account is null||account.Status!="Active")return RedirectToPage("Login");if(User.IsInRole("Employer"))return LocalRedirect("/employer");Name=account.FullName;Role=string.Join(", ",await users.GetRolesAsync(account));return Page();}
}
