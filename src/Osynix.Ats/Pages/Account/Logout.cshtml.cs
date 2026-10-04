using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osynix.Ats.Data;
namespace Osynix.Ats.Pages.Account;
[Authorize]
public class LogoutModel(SignInManager<AtsUser> signIn):PageModel
{
    public async Task<IActionResult> OnPostAsync(){await signIn.SignOutAsync();return LocalRedirect("/Account/Login");}
}
