using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
namespace Osynix.Ats.Services;
public record IntegrationEntitlement(bool Enabled,bool Zoom,bool Owner,bool Platform,Guid? WorkspaceId,string Workspace,string Reason);
public record IntegrationProvider(string Id,string Name,string Category,string Description,bool Implemented,bool RequiresZoom=false);
public record IntegrationProviderState(IntegrationProvider Provider,bool Included,string Status,string? Account=null,string? Error=null);
public record IntegrationOverview(IntegrationEntitlement Access,IReadOnlyList<IntegrationProviderState> Providers);
public static class IntegrationEntitlements {
 public static async Task<IntegrationEntitlement> Read(AtsDbContext db,ClaimsPrincipal user){
  if(!(user.IsInRole("Admin")||user.IsInRole("Employer")||user.IsInRole("Recruiter"))||!await EndpointAccess.Allowed(db,user))throw new UnauthorizedAccessException("Integration settings are unavailable for this account.");
  if(user.IsInRole("Admin"))return new(true,true,true,true,null,"Platform account","");
  var active=await WorkspaceContext.Resolve(db,user);
  if(active.Id is not Guid id)return new(false,false,false,false,null,"Current account","Your subscription does not include online interview integrations.");
  await SubscriptionLifecycle.ApplyDue(db,id);
  var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.Id==id);
  var terms=SubscriptionRules.Terms(workspace);var enabled=terms?.MeetingIntegrations==true;
  return new(enabled,enabled&&terms?.ZoomIntegration==true,active.Owner,false,id,workspace.CompanyName,enabled?"":"Your workspace subscription does not include online interview integrations.");
 }
 public static async Task<IntegrationEntitlement> Require(AtsDbContext db,ClaimsPrincipal user){var result=await Read(db,user);if(!result.Enabled)throw new UnauthorizedAccessException(result.Reason);return result;}
 public static string ContextKey(IntegrationEntitlement access)=>access.WorkspaceId?.ToString()??"platform";
}
public class IntegrationSettingsService(IDbContextFactory<AtsDbContext> factory,GoogleMeetService google){
 // Add future providers here so their cards and entitlement states share one catalogue.
 public static IReadOnlyList<IntegrationProvider> Catalogue {get;}=Array.AsReadOnly(new[]{
  new IntegrationProvider("google-meet","Google Meet","Online interviews","Create interview meetings and send Google Calendar invitations from your connected Google account.",true),
  new IntegrationProvider("microsoft-teams","Microsoft Teams","Online interviews","Connect Microsoft 365 for interview meetings and calendar invitations when this provider becomes available.",false),
  new IntegrationProvider("zoom","Zoom","Online interviews","Connect your Zoom account for interview meetings when this provider becomes available.",false,true)
 });
 public async Task<IntegrationEntitlement> Entitlement(ClaimsPrincipal user){await using var db=await factory.CreateDbContextAsync();return await IntegrationEntitlements.Read(db,user);}
 public async Task<IntegrationOverview> Load(ClaimsPrincipal user){
  await using var db=await factory.CreateDbContextAsync();var access=await IntegrationEntitlements.Read(db,user);
  if(!access.Enabled)return new(access,Catalogue.Select(p=>new IntegrationProviderState(p,false,"Not included")).ToArray());
  var uid=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var connection=await google.Connection(uid);
  var error=await db.GoogleInterviewEvents.AsNoTracking().Where(e=>e.OwnerId==uid&&!e.Deleted&&e.Error!="").Select(e=>e.Error).FirstOrDefaultAsync();
  return new(access,Catalogue.Select(p=>{
   var included=!p.RequiresZoom||access.Zoom;
   if(!included)return new IntegrationProviderState(p,false,"Not included");
   if(!p.Implemented)return new IntegrationProviderState(p,true,"Coming soon");
   return new IntegrationProviderState(p,true,connection is not null?(string.IsNullOrEmpty(error)?"Connected":"Needs attention"):google.Configured?"Not connected":"Setup required",connection?.Email,error);
  }).ToArray());
 }
 public bool GoogleConfigured=>google.Configured;
 public async Task CheckConnection(ClaimsPrincipal user,string provider){await using var db=await factory.CreateDbContextAsync();await IntegrationEntitlements.Require(db,user);if(provider!="google-meet")throw new InvalidOperationException("This provider is not available yet.");await google.VerifyConnection(user.FindFirstValue(ClaimTypes.NameIdentifier)!);}
 public async Task Disconnect(ClaimsPrincipal user,string provider){await using var db=await factory.CreateDbContextAsync();await IntegrationEntitlements.Require(db,user);if(provider!="google-meet")throw new InvalidOperationException("This provider is not available yet.");await google.Disconnect(user.FindFirstValue(ClaimTypes.NameIdentifier)!);}
}
