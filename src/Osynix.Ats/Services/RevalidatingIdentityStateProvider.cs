using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Osynix.Ats.Data;
namespace Osynix.Ats.Services;
public sealed class RevalidatingIdentityStateProvider(ILoggerFactory loggerFactory,IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval=>TimeSpan.FromMinutes(5);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state,CancellationToken token)
    {
        using var scope=scopes.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
        var user=await manager.GetUserAsync(state.User);if(user is null||await manager.IsLockedOutAsync(user))return false;
        var claim=state.User.FindFirstValue("AspNet.Identity.SecurityStamp");
        if(claim!=await manager.GetSecurityStampAsync(user))return false;
        var roles=await manager.GetRolesAsync(user);
        return roles.Order().SequenceEqual(state.User.FindAll(ClaimTypes.Role).Select(c=>c.Value).Order());
    }
}
