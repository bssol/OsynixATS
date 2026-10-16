using System.Net;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;
namespace Osynix.Ats.Tests;
public class AccountFlowTests {
 [Theory]
 [InlineData("{\"email_verified\":true}","true")]
 [InlineData("{\"email_verified\":false}","false")]
 [InlineData("{\"verified_email\":true}",null)]
 [InlineData("{}",null)]
 public void GoogleV3VerificationClaimIsMappedWithoutTrustingMissingVerification(string json,string? expected){
  var options=new Microsoft.AspNetCore.Authentication.Google.GoogleOptions();
  GoogleAccountClaims.Configure(options);
  using var document=System.Text.Json.JsonDocument.Parse(json);
  var identity=new ClaimsIdentity();
  foreach(var action in options.ClaimActions)action.Run(document.RootElement,identity,"Google");
  Assert.Equal(expected,identity.FindFirst("email_verified")?.Value,ignoreCase:true);
 }
 sealed class Mail:IAccountEmail {public bool Configured=>true;public string LastBody="";public Task Send(string email,string subject,string body){LastBody=body;return Task.CompletedTask;}}
 sealed class Host:WebApplicationFactory<AtsUser>{
  public Mail Mail=new();readonly string path=Path.Combine(Path.GetTempPath(),"osynix-auth-"+Guid.NewGuid()+".db");
  protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Development");b.ConfigureLogging(x=>x.ClearProviders());b.ConfigureServices(s=>{
   s.RemoveAll<IDbContextFactory<AtsDbContext>>();s.RemoveAll<DbContextOptions<AtsDbContext>>();s.RemoveAll<AtsDbContext>();
   s.AddDbContextFactory<AtsDbContext>(o=>o.UseSqlite("Data Source="+path));s.RemoveAll<IAccountEmail>();s.AddSingleton<IAccountEmail>(Mail);s.AddDataProtection().UseEphemeralDataProtectionProvider();
  });}
  public override async ValueTask DisposeAsync(){await base.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
 }
 static async Task<Dictionary<string,string>> Form(HttpClient c,string page,Dictionary<string,string> values){
  var html=await c.GetStringAsync(page);values["__RequestVerificationToken"]=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;return values;
 }
 [Fact]public async Task RecruiterRegistrationRequiresOtpThenCannotSeeExistingRecords(){
  await using var h=new Host();var c=h.CreateClient(new(){AllowAutoRedirect=false});
  using(var scope=h.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();db.Positions.Add(new(){Reference="001",Client="Private Client",Title="Private Role",CreatedBy="someone"});await db.SaveChangesAsync();}
  var registration=await c.PostAsync("/Account/Register",new FormUrlEncodedContent(await Form(c,"/Account/Register",new(){{"FullName","New Recruiter"},{"Email","new@example.test"},{"Role","Recruiter"},{"Password","Ab1!xy"},{"ConfirmPassword","Ab1!xy"}})));
  Assert.Equal(HttpStatusCode.Redirect,registration.StatusCode);
  using(var scope=h.Services.CreateScope()){var u=await scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>().FindByEmailAsync("new@example.test");Assert.False(u!.EmailConfirmed);Assert.Equal("Pending",u.Status);}
  var code=Regex.Match(h.Mail.LastBody,@"\b\d{6}\b").Value;Assert.Equal(6,code.Length);
  var verify=await c.PostAsync("/Account/Verify",new FormUrlEncodedContent(await Form(c,"/Account/Verify?email=new%40example.test",new(){{"Email","new@example.test"},{"Code",code}})));
  Assert.Contains("Email verified",await verify.Content.ReadAsStringAsync());
  var login=await c.PostAsync("/Account/Login",new FormUrlEncodedContent(await Form(c,"/Account/Login",new(){{"Email","new@example.test"},{"Password","Ab1!xy"}})));
  Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);
  var positions=await c.GetStringAsync("/positions");Assert.DoesNotContain("Private Role",positions);
  Assert.Contains("Manage Positions",positions);
  var settings=await c.GetAsync("/settings");Assert.Equal(HttpStatusCode.Redirect,settings.StatusCode);Assert.Contains("/denied",settings.Headers.Location!.ToString());
 }
 [Fact]public async Task EmployerRegistrationVerifiesEmailAndCreatesOnlyItsOwnWorkspace(){
  await using var h=new Host();using var c=h.CreateClient(new(){AllowAutoRedirect=false});
  var landing=await c.GetStringAsync("/Account/Login");Assert.Contains("Create An Employer Account",landing);
  var form=await c.GetStringAsync("/Account/Register?role=Employer");Assert.Contains("Employer",form);
  var registered=await c.PostAsync("/Account/Register",new FormUrlEncodedContent(await Form(c,"/Account/Register?role=Employer",new(){{"FullName","Employer Owner"},{"Email","owner@example.test"},{"Role","Employer"},{"Password","Ab1!xy"},{"ConfirmPassword","Ab1!xy"}})));
  Assert.Equal(HttpStatusCode.Redirect,registered.StatusCode);
  var code=Regex.Match(h.Mail.LastBody,@"\b\d{6}\b").Value;
  await c.PostAsync("/Account/Verify",new FormUrlEncodedContent(await Form(c,"/Account/Verify?email=owner%40example.test",new(){{"Email","owner@example.test"},{"Code",code}})));
  var login=await c.PostAsync("/Account/Login",new FormUrlEncodedContent(await Form(c,"/Account/Login",new(){{"Email","owner@example.test"},{"Password","Ab1!xy"}})));Assert.Equal("/employer",login.Headers.Location!.ToString());
  foreach(var page in new[]{"/employer","/employer/company","/employer/subscriptions","/employer/team","/profile"}){var response=await c.GetAsync(page);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("The page could not load",html);Assert.DoesNotContain("role=\"alert\">Error",html);}
  Assert.Contains("Employer Owner",await c.GetStringAsync("/employer"));
  using var scope=h.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var w=Assert.Single(await db.EmployerWorkspaces.ToListAsync());Assert.Equal(1,w.FreeRecruiterSeats);Assert.Equal(3,w.FreePositionSlots);Assert.False(w.TeamTalentPoolEnabled);Assert.False(w.CareersPublished);
  using var guest=h.CreateClient();Assert.DoesNotContain("Employer Owner",await guest.GetStringAsync("/careers/"+w.Id));
 }
 sealed class TeamState(string id,string role):AuthenticationStateProvider{public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,id),new(ClaimTypes.Role,role)],"Test"))));}
 static async Task<(EmployerTeamService Teams,string Owner)> TeamService(Host host){
  using var scope=host.Services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var owner=new AtsUser{UserName="invite-owner@example.test",Email="invite-owner@example.test",FullName="Invite Company",EmailConfirmed=true};Assert.True((await manager.CreateAsync(owner,"Ab1!xy")).Succeeded);Assert.True((await manager.AddToRoleAsync(owner,"Employer")).Succeeded);
  var factory=host.Services.GetRequiredService<IDbContextFactory<AtsDbContext>>();var state=new TeamState(owner.Id,"Employer");var service=new EmployerService(state,new Access(state,factory),factory);return(new EmployerTeamService(service,factory,host.Services.GetRequiredService<IDataProtectionProvider>(),new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),host.Mail),owner.Id);
 }
 [Fact]public async Task InvitedRecruiterCanRegisterVerifyAcceptAndRemainSignedInThenLoseAccessOnDeactivation(){
  await using var h=new Host();using var c=h.CreateClient(new(){AllowAutoRedirect=false});var (teams,owner)=await TeamService(h);var invitation=await teams.Invite("Invited Recruiter","invited@example.test");var uri=new Uri(invitation.Link);var token=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)["token"].ToString();var registerUrl="/Account/Register?role=Recruiter&invite="+token;
  Assert.Contains("invited@example.test",await c.GetStringAsync(registerUrl));
  var registered=await c.PostAsync(registerUrl,new FormUrlEncodedContent(await Form(c,registerUrl,new(){{"FullName","Invited Recruiter"},{"Email","invited@example.test"},{"Role","Recruiter"},{"Password","Ab1!xy"},{"ConfirmPassword","Ab1!xy"},{"Invite",token}})));Assert.Equal(HttpStatusCode.Redirect,registered.StatusCode);
  var code=Regex.Match(h.Mail.LastBody,@"\b\d{6}\b").Value;var verifyUrl=registered.Headers.Location!.ToString();await c.PostAsync(verifyUrl,new FormUrlEncodedContent(await Form(c,verifyUrl,new(){{"Email","invited@example.test"},{"Code",code},{"Invite",token}})));
  var loginUrl="/Account/Login?returnUrl="+Uri.EscapeDataString(uri.PathAndQuery);var login=await c.PostAsync(loginUrl,new FormUrlEncodedContent(await Form(c,loginUrl,new(){{"Email","invited@example.test"},{"Password","Ab1!xy"}})));Assert.Equal(uri.PathAndQuery,login.Headers.Location!.ToString());
  Assert.Contains("Accept Invitation",await c.GetStringAsync(uri.PathAndQuery));var accepted=await c.PostAsync(uri.PathAndQuery,new FormUrlEncodedContent(await Form(c,uri.PathAndQuery,new(){{"Token",token}})));Assert.Equal("/",accepted.Headers.Location!.ToString());Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/positions")).StatusCode);
  var member=Assert.Single((await teams.Team()).Members);Assert.False(member.CanCreateClients);await teams.Update(member with{Status="Inactive"});var denied=await c.GetAsync("/positions");Assert.Equal(HttpStatusCode.OK,denied.StatusCode);Assert.Contains("Team Access Is Inactive",await denied.Content.ReadAsStringAsync());Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/Account/Workspaces")).StatusCode);
 }
 [Fact]public async Task SimultaneousInvitationAcceptanceCannotExceedOneFreeSeat(){
  await using var h=new Host();h.CreateClient();var (teams,owner)=await TeamService(h);var principals=new List<ClaimsPrincipal>();var tokens=new List<string>();
  for(int i=0;i<2;i++){using var scope=h.Services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var user=new AtsUser{UserName=$"race{i}@example.test",Email=$"race{i}@example.test",EmailConfirmed=true};Assert.True((await manager.CreateAsync(user,"Ab1!xy")).Succeeded);Assert.True((await manager.AddToRoleAsync(user,"Recruiter")).Succeeded);principals.Add((await new TeamState(user.Id,"Recruiter").GetAuthenticationStateAsync()).User);var result=await teams.Invite("Race Recruiter",user.Email);tokens.Add(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(result.Link).Query)["token"].ToString());}
  async Task<bool> Accept(int i){try{await teams.Accept(tokens[i],principals[i]);return true;}catch(InvalidOperationException){return false;}}
  var results=await Task.WhenAll(Accept(0),Accept(1));Assert.Single(results,x=>x);Assert.Single((await teams.Team()).Members);
 }
 [Fact]public async Task OtpIsOneTimeAndLocksAfterFiveIncorrectAttempts(){
  await using var h=new Host();h.CreateClient();using var scope=h.Services.CreateScope();
  var service=scope.ServiceProvider.GetRequiredService<AccountRegistration>();var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
  await service.Register("otp@example.test","OTP User","Strong-test!123","Candidate");
  var user=(await users.FindByEmailAsync("otp@example.test"))!;var code=Regex.Match(h.Mail.LastBody,@"\b\d{6}\b").Value;
  for(int i=0;i<5;i++)Assert.False(await service.Verify(user,"wrong"));
  Assert.False(await service.Verify(user,code));
  await service.Register("valid@example.test","Valid","Strong-test!123","Candidate");
  user=(await users.FindByEmailAsync("valid@example.test"))!;code=Regex.Match(h.Mail.LastBody,@"\b\d{6}\b").Value;
  Assert.True(await service.Verify(user,code));Assert.False(await service.Verify(user,code));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>service.Register("admin@example.test","Admin","Strong-test!123","Admin"));
 }
 [Fact]public async Task VerifiedEmployerCannotReadRecruiterDataAndPasswordResetWorks(){
  await using var h=new Host();var c=h.CreateClient(new(){AllowAutoRedirect=false});
  using(var scope=h.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var u=new AtsUser{UserName="employer@example.test",Email="employer@example.test",EmailConfirmed=true};Assert.True((await users.CreateAsync(u,"Strong-test!123")).Succeeded);await users.AddToRoleAsync(u,"Employer");}
  await c.PostAsync("/Account/ForgotPassword",new FormUrlEncodedContent(await Form(c,"/Account/ForgotPassword",new(){{"Email","employer@example.test"}})));
  var link=Regex.Match(h.Mail.LastBody,@"http://localhost:5080/Account/ResetPassword\?[^\s]+").Value;Assert.NotEmpty(link);
  var uri=new Uri(link);var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
  var reset=await c.PostAsync("/Account/ResetPassword",new FormUrlEncodedContent(await Form(c,uri.PathAndQuery,new(){{"Email","employer@example.test"},{"Token",query["token"].ToString()},{"Password","Changed-test!123"},{"ConfirmPassword","Changed-test!123"}})));
  Assert.Contains("password has been reset",await reset.Content.ReadAsStringAsync());
  var login=await c.PostAsync("/Account/Login",new FormUrlEncodedContent(await Form(c,"/Account/Login",new(){{"Email","employer@example.test"},{"Password","Changed-test!123"}})));
  Assert.Equal("/employer",login.Headers.Location!.ToString());
  var ownPositions=await c.GetAsync("/positions");Assert.Equal(HttpStatusCode.OK,ownPositions.StatusCode);Assert.Contains("+ Create Position",WebUtility.HtmlDecode(await ownPositions.Content.ReadAsStringAsync()));
  var portal=await c.GetStringAsync("/employer");Assert.Contains("action=\"/Account/Logout\"",portal);
  var invalidLogout=await c.PostAsync("/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string>()));
  Assert.Equal(HttpStatusCode.BadRequest,invalidLogout.StatusCode);
  var logout=await c.PostAsync("/Account/Logout",new FormUrlEncodedContent(await Form(c,"/employer",new())));
  Assert.Equal(HttpStatusCode.Redirect,logout.StatusCode);Assert.Equal("/Account/Login",logout.Headers.Location!.ToString());
  var afterLogout=await c.GetAsync("/Account/Portal");
  Assert.Equal(HttpStatusCode.Redirect,afterLogout.StatusCode);Assert.Contains("/Account/Login",afterLogout.Headers.Location!.ToString());
 }
 [Fact]public async Task DeletedAccountCannotReuseItsSignedInSession(){
  await using var h=new Host();var c=h.CreateClient(new(){AllowAutoRedirect=false});
  using(var scope=h.Services.CreateScope()){
   var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
   var user=new AtsUser{UserName="removed@example.test",Email="removed@example.test",EmailConfirmed=true,Status="Active"};
   Assert.True((await users.CreateAsync(user,"Strong-test!123")).Succeeded);await users.AddToRoleAsync(user,"Employer");
  }
  var login=await c.PostAsync("/Account/Login",new FormUrlEncodedContent(await Form(c,"/Account/Login",new(){{"Email","removed@example.test"},{"Password","Strong-test!123"}})));
  Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);
  Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync("/Account/Portal")).StatusCode);
  using(var scope=h.Services.CreateScope()){
   var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
   Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("removed@example.test"))!)).Succeeded);
  }
  var response=await c.GetAsync("/Account/Portal");
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Contains("/Account/Login",response.Headers.Location!.ToString());
 }

 [Fact]public async Task GoogleAccountCanSetAndChangeAtsPasswordWithoutLosingGoogle(){
  await using var h=new Host();var c=h.CreateClient(new(){AllowAutoRedirect=false});
  using(var scope=h.Services.CreateScope()){
   var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
   var user=new AtsUser{UserName="different-username",Email="google@example.test",EmailConfirmed=true,Status="Active"};
   Assert.True((await users.CreateAsync(user)).Succeeded);await users.AddToRoleAsync(user,"Employer");
   Assert.True((await users.AddLoginAsync(user,new UserLoginInfo("Google","test-google-id","Google"))).Succeeded);
  }
  // A valid Google external cookie represents the already verified OAuth callback.
  c.Dispose();
  using(var scope=h.Services.CreateScope()){
   var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
   var user=(await users.FindByEmailAsync("google@example.test"))!;
   var principal=await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AtsUser>>().CreateAsync(user);
   var options=scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
   var ticket=new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal,new Microsoft.AspNetCore.Authentication.AuthenticationProperties(),IdentityConstants.ApplicationScheme);
   c=h.CreateClient(new(){AllowAutoRedirect=false});c.DefaultRequestHeaders.Add("Cookie",options.Cookie.Name+"="+options.TicketDataFormat.Protect(ticket));
  }
  Assert.Contains("Set Password",await c.GetStringAsync("/Account/Password"));
  var set=await c.PostAsync("/Account/Password",new FormUrlEncodedContent(await Form(c,"/Account/Password",new(){{"NewPassword","Strong-test!123"},{"ConfirmPassword","Strong-test!123"}})));
  Assert.Contains("password is saved",await set.Content.ReadAsStringAsync());
  c.DefaultRequestHeaders.Remove("Cookie");c.DefaultRequestHeaders.Add("Cookie",set.Headers.GetValues("Set-Cookie").First(x=>x.StartsWith(".AspNetCore.Identity.Application=")).Split(';')[0]);
  Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync("/Account/Portal")).StatusCode);
  var wrong=await c.PostAsync("/Account/Password",new FormUrlEncodedContent(await Form(c,"/Account/Password",new(){{"CurrentPassword","Wrong-test!123"},{"NewPassword","Changed-test!123"},{"ConfirmPassword","Changed-test!123"}})));
  Assert.DoesNotContain("password is saved",await wrong.Content.ReadAsStringAsync());
  var change=await c.PostAsync("/Account/Password",new FormUrlEncodedContent(await Form(c,"/Account/Password",new(){{"CurrentPassword","Strong-test!123"},{"NewPassword","Changed-test!123"},{"ConfirmPassword","Changed-test!123"}})));
  Assert.Contains("password is saved",await change.Content.ReadAsStringAsync());
  using(var scope=h.Services.CreateScope()){
   var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();var user=(await users.FindByEmailAsync("google@example.test"))!;
   Assert.False(await users.CheckPasswordAsync(user,"Strong-test!123"));Assert.True(await users.CheckPasswordAsync(user,"Changed-test!123"));
   Assert.Single(await users.GetLoginsAsync(user));
  }
  var fresh=h.CreateClient(new(){AllowAutoRedirect=false});
  var login=await fresh.PostAsync("/Account/Login",new FormUrlEncodedContent(await Form(fresh,"/Account/Login",new(){{"Email","google@example.test"},{"Password","Changed-test!123"}})));
  Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);Assert.Equal("/employer",login.Headers.Location!.ToString());
 }

 [Fact]public async Task BrandedPagesRenderWithoutPublicRecruitmentData(){
  await using var h=new Host();var c=h.CreateClient();
  var html=await c.GetStringAsync("/Account/Login");Assert.Contains("auth-split",html);Assert.Contains("osynix-group-logo.png",html);Assert.Contains("Forgot Password?",html);Assert.Contains("Continue With Google",html);
  Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/Account/Register")).StatusCode);
  var css=await c.GetStringAsync("/auth.css");Assert.Contains("grid-template-columns:7fr 3fr",css);
 }
}
