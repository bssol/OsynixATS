using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
namespace Osynix.Ats.Services;
public static class GoogleAccountClaims {
 public static void Configure(GoogleOptions options) =>
  options.ClaimActions.MapJsonKey("email_verified","email_verified");
}
