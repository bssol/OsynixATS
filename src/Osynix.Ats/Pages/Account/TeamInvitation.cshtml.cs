using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Pages.Account;
[AllowAnonymous,EnableRateLimiting("auth")]
public class TeamInvitationModel(EmployerTeamService teams,Microsoft.EntityFrameworkCore.IDbContextFactory<AtsDbContext> factory,SignInManager<AtsUser> signIn):PageModel
{
    [BindProperty(SupportsGet=true)]public string? Token{get;set;}
    public InvitationView? Invitation{get;set;}
    public string ReturnUrl=>"/Account/TeamInvitation?token="+Uri.EscapeDataString(Token??"");
    public async Task OnGetAsync()=>Invitation=await teams.Read(Token);
    public async Task<IActionResult> OnPostAsync(){
        Invitation=await teams.Read(Token);
        try{var workspaceId=await teams.Accept(Token,User);await using var db=await factory.CreateDbContextAsync();var account=await db.Users.FindAsync(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);if(account is not null)await signIn.SignInWithClaimsAsync(account,false,[new System.Security.Claims.Claim(WorkspaceContext.Claim,workspaceId.ToString())]);return LocalRedirect("/");}
        catch(Exception e)when(e is InvalidOperationException or UnauthorizedAccessException){ModelState.AddModelError("",e.Message);return Page();}
    }
}
