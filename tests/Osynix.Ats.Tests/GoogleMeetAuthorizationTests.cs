using System.Net;
using System.Text.Json;
using System.Security.Claims;
using Osynix.Ats.Domain;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
using Xunit;
namespace Osynix.Ats.Tests;
public class GoogleMeetAuthorizationTests {
 [Fact]public void MeetCredentialsUseSavedGoogleFallbackButNeverMixPartialClients(){
  IConfiguration Config(Dictionary<string,string?> values)=>new ConfigurationBuilder().AddInMemoryCollection(values).Build();
  var config=Config(new(){{"Authentication:Google:ClientId","saved-id"},{"Authentication:Google:ClientSecret","saved-secret"}});Assert.True(GoogleMeetService.HasClientConfiguration(config));Assert.Equal(("saved-id","saved-secret"),GoogleMeetService.ClientSettings(config));
  config=Config(new(){{"Authentication:Google:ClientId","saved-id"},{"Authentication:Google:ClientSecret","saved-secret"},{"GoogleMeet:ClientId","meet-id"},{"GoogleMeet:ClientSecret","meet-secret"}});Assert.Equal(("meet-id","meet-secret"),GoogleMeetService.ClientSettings(config));
  config=Config(new(){{"Authentication:Google:ClientId","saved-id"},{"Authentication:Google:ClientSecret","saved-secret"},{"GoogleMeet:ClientId","partial-meet"}});Assert.False(GoogleMeetService.HasClientConfiguration(config));Assert.Equal(("partial-meet",""),GoogleMeetService.ClientSettings(config));
 }
 sealed class GoogleBackchannel:HttpMessageHandler {
  public bool GrantScope=true;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   var json=request.RequestUri!.Host=="oauth2.googleapis.com"?"{\"access_token\":\"fixture-access\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"fixture-refresh\",\"scope\":\""+(GrantScope?GoogleMeetService.Scope+" "+GoogleMeetService.GmailProposalScope:"email")+"\"}":"{\"sub\":\"google-subject\",\"email\":\"organizer@example.test\",\"email_verified\":true,\"name\":\"Organizer\"}";
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json")});
  }
 }
 sealed class Host:WebApplicationFactory<AtsUser>{
  readonly string path=Path.Combine(Path.GetTempPath(),"osynix-google-auth-"+Guid.NewGuid()+".db");public GoogleBackchannel Google=new();
  protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Development");b.ConfigureLogging(x=>x.ClearProviders());b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"GoogleMeet:ClientId","fixture-client"},{"GoogleMeet:ClientSecret","fixture-secret"}}));b.ConfigureServices(s=>{
   s.RemoveAll<IDbContextFactory<AtsDbContext>>();s.RemoveAll<DbContextOptions<AtsDbContext>>();s.RemoveAll<AtsDbContext>();s.AddDbContextFactory<AtsDbContext>(o=>o.UseSqlite("Data Source="+path));s.AddDataProtection().UseEphemeralDataProtectionProvider();
   var oauth=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"GoogleMeet:ClientId","fixture-client"},{"GoogleMeet:ClientSecret","fixture-secret"}}).Build();
   using var inspection=s.BuildServiceProvider();var schemes=inspection.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
   if(!schemes.SchemeMap.ContainsKey(GoogleMeetService.Scheme))s.AddAuthentication().AddCookie("GoogleMeetExternal").AddGoogle(GoogleMeetService.Scheme,o=>GoogleMeetService.Configure(o,oauth));
   s.Configure<GoogleOptions>(GoogleMeetService.Scheme,o=>{GoogleMeetService.Configure(o,oauth);o.Backchannel=new HttpClient(Google);});
  });}
  public override async ValueTask DisposeAsync(){await base.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
 }
 static async Task<string> Csrf(HttpClient client,string page)=>Regex.Match(await client.GetStringAsync(page),"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
 static async Task<HttpClient> Login(Host h,string role="Admin",string accountType=AccountTypes.Legacy){var c=h.CreateClient(new(){AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});using(var scope=h.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var account=new AtsUser{UserName="google-admin@example.test",Email="google-admin@example.test",EmailConfirmed=true,AccountType=accountType};Assert.True((await users.CreateAsync(account,"Test-password!123")).Succeeded);await users.AddToRoleAsync(account,role);}
  var result=await c.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","google-admin@example.test"},{"Password","Test-password!123"},{"__RequestVerificationToken",await Csrf(c,"/Account/Login")}}));Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);return c;}
 static async Task<Guid> Plan(Host h,bool enabled,bool zoom=false){using var scope=h.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var u=await db.Users.SingleAsync(u=>u.UserName=="google-admin@example.test");var w=await EmployerService.EnsureWorkspace(db,u);w.SubscriptionTermsJson=JsonSerializer.Serialize(new SubscriptionTerms{MeetingIntegrations=enabled,ZoomIntegration=zoom});await db.SaveChangesAsync();return w.Id;}
 [Theory][InlineData("Employer",AccountTypes.Employer,true)][InlineData("Employer",AccountTypes.Employer,false)][InlineData("Recruiter",AccountTypes.Freelance,true)][InlineData("Recruiter",AccountTypes.Freelance,false)]
 public async Task IntegrationTabAndDirectGoogleConnectionFollowWorkspacePlan(string role,string accountType,bool enabled){
  await using var h=new Host();using var c=await Login(h,role,accountType);await Plan(h,enabled);
  var html=await c.GetStringAsync("/settings/integrations?provider=google-meet");
  if(enabled){Assert.Contains("Connect Google Meet",html);Assert.Contains("Microsoft Teams",html);Assert.Contains("Coming soon",html);Assert.Contains("Not included",html);Assert.Contains("href=\"/settings/integrations\"",html);Assert.DoesNotContain("fixture-secret",html);}
  else{Assert.Contains("Online interview integrations are not included",html);Assert.Contains("aria-disabled=\"true\"",html);Assert.DoesNotContain("Connect Google Meet",html);}
  var csrf=await Csrf(c,enabled?"/settings/integrations?provider=google-meet":"/Account/ForgotPassword");Assert.NotEmpty(csrf);
  var post=await c.PostAsync("/integrations/google-meet",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf}}));
  if(enabled){Assert.Equal(HttpStatusCode.Redirect,post.StatusCode);Assert.StartsWith("https://accounts.google.com/",post.Headers.Location!.ToString());}
  else{Assert.True(post.StatusCode==HttpStatusCode.Forbidden||post.StatusCode==HttpStatusCode.Redirect&&post.Headers.Location!.ToString().Contains("denied"));}
 }
 [Fact]public async Task DowngradeDuringOAuthPreventsStoringConnection(){
  await using var h=new Host();using var c=await Login(h,"Employer",AccountTypes.Employer);await Plan(h,true);
  var challenge=await c.PostAsync("/integrations/google-meet",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",await Csrf(c,"/settings/integrations?provider=google-meet")}}));var query=QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
  await Plan(h,false);var callback=await c.GetAsync("/signin-google-meet?code=fixture-code&state="+Uri.EscapeDataString(query["state"].ToString()));Assert.Contains("error=authorization",callback.Headers.Location!.ToString());using var scope=h.Services.CreateScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<AtsDbContext>().Set<IdentityUserToken<string>>().Where(t=>t.LoginProvider==GoogleMeetService.Scheme).ToListAsync());
 }
 [Fact]public async Task LegacyGoogleSettingsRedirectIntoHubAndConnectedAccountRemainsPrivate(){
  await using var h=new Host();using var c=await Login(h);var old=await c.GetAsync("/integrations/google-meet");Assert.Equal("/settings/integrations?provider=google-meet",old.Headers.Location!.ToString());
  using var scope=h.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var user=await db.Users.SingleAsync(u=>u.UserName=="google-admin@example.test");var meet=scope.ServiceProvider.GetRequiredService<GoogleMeetService>();await meet.Connect(user.Id,"google-owner","private-organizer@example.test","private-refresh");
  var html=await c.GetStringAsync("/settings/integrations?provider=google-meet");Assert.Contains("private-organizer@example.test",html);Assert.Contains("Reconnect Google Meet",html);Assert.DoesNotContain("private-refresh",html);
  var anonymous=h.CreateClient(new(){AllowAutoRedirect=false});var denied=await anonymous.GetAsync("/settings/integrations");Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode);
 }
 [Fact]public async Task SelectedWorkspaceCannotBorrowAnotherWorkspacesIntegrationPlan(){
  await using var h=new Host();using var c=await Login(h,"Recruiter",AccountTypes.Freelance);await Plan(h,true,true);using var scope=h.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var user=await db.Users.SingleAsync(u=>u.UserName=="google-admin@example.test");
  var owner=new AtsUser{Id="other-owner",UserName="other-owner",Email="other@example.test",EmailConfirmed=true,AccountType=AccountTypes.Employer};db.Users.Add(owner);db.UserRoles.Add(new(){UserId=owner.Id,RoleId=(await db.Roles.SingleAsync(r=>r.Name=="Employer")).Id});var other=new EmployerWorkspace{OwnerId=owner.Id,CompanyName="Other Company",SubscriptionTermsJson=JsonSerializer.Serialize(new SubscriptionTerms{MeetingIntegrations=false})};db.Add(other);db.WorkspaceMemberships.Add(new(){WorkspaceId=other.Id,UserId=user.Id,Status="Active"});await db.SaveChangesAsync();
  var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim(ClaimTypes.Name,user.UserName!),new Claim(ClaimTypes.Role,"Recruiter"),new Claim(WorkspaceContext.Claim,other.Id.ToString())},"test"));var access=await IntegrationEntitlements.Read(db,principal);Assert.False(access.Enabled);Assert.Equal(other.Id,access.WorkspaceId);
 }
 [Theory][InlineData(true)][InlineData(false)]
 public async Task CallbackUsesSignedInOrganizerAndRequiresCalendarPermission(bool grant){
  await using var h=new Host();h.Google.GrantScope=grant;using var c=await Login(h);
  var missingCsrf=await c.PostAsync("/integrations/google-meet",new FormUrlEncodedContent(new Dictionary<string,string>()));Assert.Equal(HttpStatusCode.BadRequest,missingCsrf.StatusCode);
  var challenge=await c.PostAsync("/integrations/google-meet",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",await Csrf(c,"/settings/integrations?provider=google-meet")}}));Assert.True(challenge.StatusCode==HttpStatusCode.Redirect,await challenge.Content.ReadAsStringAsync());
  var query=QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);Assert.Contains(GoogleMeetService.Scope,query["scope"].ToString());Assert.Contains(GoogleMeetService.GmailProposalScope,query["scope"].ToString());Assert.Equal("offline",query["access_type"].ToString());Assert.EndsWith("/signin-google-meet",query["redirect_uri"].ToString());
  var callback=await c.GetAsync("/signin-google-meet?code=fixture-code&state="+Uri.EscapeDataString(query["state"].ToString()));Assert.Equal(HttpStatusCode.Redirect,callback.StatusCode);
  using var scope=h.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var user=await db.Users.SingleAsync(x=>x.UserName=="google-admin@example.test");var connection=await scope.ServiceProvider.GetRequiredService<GoogleMeetService>().Connection(user.Id);
  if(grant){Assert.Equal("/settings/integrations?provider=google-meet&connected=1",callback.Headers.Location!.ToString());Assert.Equal("google-subject",connection!.Subject);Assert.Equal("organizer@example.test",connection.Email);Assert.DoesNotContain("fixture-refresh",await c.GetStringAsync("/settings/integrations?provider=google-meet"));}
  else{Assert.Contains("error=authorization",callback.Headers.Location!.ToString());Assert.Null(connection);}
 }
 [Fact]public async Task ForgedCallbackAndAnonymousConnectionCannotStoreCredentials(){await using var h=new Host();using var c=h.CreateClient(new(){AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});var page=await c.GetAsync("/integrations/google-meet");Assert.Equal(HttpStatusCode.Redirect,page.StatusCode);var callback=await c.GetAsync("/signin-google-meet?code=fixture-code&state=forged");Assert.True(callback.Headers.Location is not null,await callback.Content.ReadAsStringAsync());Assert.Contains("error=authorization",callback.Headers.Location!.ToString());using var scope=h.Services.CreateScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<AtsDbContext>().Set<IdentityUserToken<string>>().Where(t=>t.LoginProvider==GoogleMeetService.Scheme).ToListAsync());}
}
