using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
namespace Osynix.Ats.Services;
public class Access(AuthenticationStateProvider state)
{
    public async Task<ClaimsPrincipal> Require(bool admin = false)
    {
        var user = (await state.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || !(user.IsInRole("Admin") || (!admin && user.IsInRole("Recruiter"))))
            throw new UnauthorizedAccessException("You do not have permission for this action.");
        return user;
    }
    public async Task<string> UserId() => (await Require()).FindFirstValue(ClaimTypes.NameIdentifier)!;
}
