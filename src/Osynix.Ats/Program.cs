using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Osynix.Ats.Components;
using Osynix.Ats.Data;
using Osynix.Ats.Services;

if(args.Contains("--install-browser")) return Microsoft.Playwright.Program.Main(["install","--with-deps","chromium"]);
var builder=WebApplication.CreateBuilder(args);
// Load component styles when the built app is started without a launch profile.
builder.WebHost.UseStaticWebAssets();
builder.Configuration.AddJsonFile("appsettings.Local.json",optional:true,reloadOnChange:true).AddEnvironmentVariables();
var openAiKey=Environment.GetEnvironmentVariable("OpenAI__ApiKey")??Environment.GetEnvironmentVariable("OpenAI_APIKey");
if(string.IsNullOrWhiteSpace(openAiKey)&&OperatingSystem.IsWindows())openAiKey=Environment.GetEnvironmentVariable("OpenAI__ApiKey",EnvironmentVariableTarget.User)??Environment.GetEnvironmentVariable("OpenAI_APIKey",EnvironmentVariableTarget.User);
if(!string.IsNullOrWhiteSpace(openAiKey))builder.Configuration["OpenAI:ApiKey"]=openAiKey.Trim();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",LogLevel.Warning);
if(args.Contains("--verify-smtp")){
 try{await new AccountEmail(builder.Configuration).CheckConnection();Console.WriteLine("SMTP_AUTH_OK");return 0;}
 catch(Exception e){Console.WriteLine("SMTP_AUTH_FAILED: "+e.GetType().Name);return 1;}
}
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath,"App_Data"));
var sqlite=new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Ats"));
if(sqlite.DataSource!=":memory:" && !Path.IsPathRooted(sqlite.DataSource)) sqlite.DataSource=Path.GetFullPath(sqlite.DataSource,builder.Environment.ContentRootPath);
if(sqlite.DataSource!=":memory:") Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
builder.Services.AddDbContextFactory<AtsDbContext>(o=>o.UseSqlite(sqlite.ConnectionString));
builder.Services.AddIdentity<AtsUser,IdentityRole>(o=>{
    o.Password.RequiredLength=6; o.User.RequireUniqueEmail=true;
    o.Lockout.MaxFailedAccessAttempts=5; o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AtsDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(o=>{o.ValidationInterval=TimeSpan.Zero;o.OnRefreshingPrincipal=context=>{var selected=context.CurrentPrincipal?.FindFirst(WorkspaceContext.Claim);if(context.NewPrincipal?.Identity is System.Security.Claims.ClaimsIdentity identity){if(selected is not null)identity.AddClaim(selected);var mode=context.CurrentPrincipal?.FindFirst(ProfileModes.Claim);if(mode is not null)identity.AddClaim(mode);}return Task.CompletedTask;};});
builder.Services.ConfigureApplicationCookie(o=>{o.LoginPath="/Account/Login";o.AccessDeniedPath="/denied";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Lax;o.ExpireTimeSpan=TimeSpan.FromHours(8);o.SlidingExpiration=true;});
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.Warning);
builder.Services.Configure<DataProtectionTokenProviderOptions>(o=>o.TokenLifespan=TimeSpan.FromHours(1));
builder.Services.AddScoped<IAccountEmail,AccountEmail>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<AccountRegistration>();
if(!string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientId"])&&!string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientSecret"])){
 builder.Services.AddAuthentication().AddGoogle(o=>{
  o.ClientId=builder.Configuration["Authentication:Google:ClientId"]!;
  o.ClientSecret=builder.Configuration["Authentication:Google:ClientSecret"]!;
  GoogleAccountClaims.Configure(o);
  o.Events.OnRemoteFailure=context=>{context.Response.Redirect("/Account/Google?error=failed");context.HandleResponse();return Task.CompletedTask;};
 });
}
builder.Services.AddRateLimiter(o=>{
 o.RejectionStatusCode=429;
 o.AddPolicy("review",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=120,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
 o.AddPolicy("auth",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents().AddHubOptions(options=>options.MaximumReceiveMessageSize=2*1024*1024);
builder.Services.AddRazorPages();
builder.Services.AddScoped<AuthenticationStateProvider,RevalidatingIdentityStateProvider>();
builder.Services.AddScoped<AdminService>(); builder.Services.AddScoped<Access>(); builder.Services.AddScoped<AtsService>(); builder.Services.AddScoped<WorkbookImport>(); builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<EmployerService>();
builder.Services.AddScoped<RecruiterWorkService>();
builder.Services.AddScoped<RecruitmentAttributionService>();
builder.Services.AddScoped<TalentAdministration>();
builder.Services.AddScoped<EmployerTeamService>();
builder.Services.AddScoped<FreelancerDirectory>();
builder.Services.AddScoped<MarketplaceMessages>();
builder.Services.AddScoped<SelfProfileService>();
builder.Services.AddScoped<ProfessionalProfileService>();
builder.Services.AddScoped<RecruiterActionService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<HiringReviewService>();builder.Services.AddSingleton<RecruitmentUpdates>();
builder.Services.AddScoped<IDocumentStore,DatabaseDocumentStore>();
builder.Services.AddHttpClient<AiService>(c=>c.Timeout=TimeSpan.FromMinutes(4));
builder.Services.AddSingleton<CvProfileExtractionQueue>();
builder.Services.AddSingleton<ICvProfileExtractionQueue>(sp=>sp.GetRequiredService<CvProfileExtractionQueue>());
builder.Services.AddHostedService(sp=>sp.GetRequiredService<CvProfileExtractionQueue>());
var app=builder.Build();
using(var scope=app.Services.CreateScope()) {
    var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach(var role in new[]{"Admin","SubAdmin","Recruiter","Employer","Candidate"}) if(!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new(role));
    await WorkspaceUpgrade.Apply(db);
    await CandidateCityUpgrade.Apply(db);
    await RecruiterActionService.Backfill(db);
    var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
    var email=builder.Configuration["Bootstrap:Email"]; var password=builder.Configuration["Bootstrap:Password"];
    if(!await users.Users.AnyAsync()&&!string.IsNullOrWhiteSpace(email)&&!string.IsNullOrWhiteSpace(password)) {
        var user=new AtsUser{UserName=email,Email=email,EmailConfirmed=true}; var result=await users.CreateAsync(user,password);
        if(!result.Succeeded) throw new InvalidOperationException("Administrator bootstrap failed: "+string.Join("; ",result.Errors.Select(e=>e.Description)));
        await users.AddToRoleAsync(user,"Admin");
    }
}
if(!app.Environment.IsDevelopment()) {app.UseExceptionHandler("/error");app.UseHsts();app.UseHttpsRedirection();}
app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();app.UseAntiforgery();
app.MapRazorPages();app.MapHiringReview();app.MapInterviews();
app.MapGet("/messages/attachments/{id:guid}", async Task<IResult>(Guid id, IDbContextFactory<AtsDbContext> factory, HttpContext context) => {
    await using var db = await factory.CreateDbContextAsync();
    var file = await MarketplaceMessages.Download(db, context.User, id);
    if (file?.AttachmentContent is not { Length: > 0 }) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(file.AttachmentContent, file.AttachmentType, file.AttachmentName);
}).RequireAuthorization();
app.MapGet("/careers/{id:guid}/cover",async Task<IResult>(Guid id,EmployerService employers,HttpContext context)=>{
    var company=await employers.PublicCompany(id);
    if(company is null||company.CareersCoverMode!="Upload"||company.CareersCoverContent is not {Length:>0})return Results.NotFound();
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers.CacheControl="no-store";
    return Results.File(company.CareersCoverContent,company.CareersCoverContentType);
}).AllowAnonymous();
app.MapGet("/files/{id:guid}",async Task<IResult>(Guid id,bool? view,IDbContextFactory<AtsDbContext> factory,IDocumentStore storage,HttpContext context)=>{
    await using var db=await factory.CreateDbContextAsync();
    if(!await EndpointAccess.Allowed(db,context.User)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if(Access.Scoped(context.User)){
        var uid=context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;var name=context.User.Identity!.Name;var workspaceId=await EndpointAccess.WorkspaceId(db,context.User);
        var owner=(await WorkspaceContext.Resolve(db,context.User)).Owner;
        var profileMode=await ProfileModes.Selected(db,context.User,await WorkspaceContext.Resolve(db,context.User));
        var ownJd=await db.Positions.AnyAsync(p=>p.JdDocumentId==id&&(profileMode==null||p.IsAgencyPosition==profileMode)&&((owner||(p.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==p.Id&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name))))&&p.WorkspaceId==workspaceId));
        var ownCv=await db.Assessments.AnyAsync(a=>(a.DocumentId==id||a.Candidate!.DocumentId==id)&&(profileMode==null||a.Position!.IsAgencyPosition==profileMode)&&((owner||(a.Position!.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==a.PositionId&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name))))&&a.Position!.WorkspaceId==workspaceId));
        var active=await WorkspaceContext.Resolve(db,context.User);
        var ownPool=active.Legacy?EndpointAccess.PoolCandidates(db,workspaceId):db.Candidates.Where(c=>c.WorkspaceId==workspaceId);
        var poolCv=await EndpointAccess.Allowed(db,context.User,"Talent.View")&&await ownPool.AnyAsync(c=>c.DocumentId==id);
        poolCv=poolCv||(await EndpointAccess.Allowed(db,context.User,"Talent.Master")&&await TalentIdentity.MasterCandidates(db).AnyAsync(c=>c.DocumentId==id));
        if(!ownJd&&!ownCv&&!poolCv)return Results.StatusCode(403);
    }
    var doc=await storage.Read(id);
    context.Response.Headers.CacheControl="no-store";
    return doc is null?Results.NotFound():Results.File(doc.Content,doc.ContentType,view==true?null:doc.FileName);
}).RequireAuthorization(p=>p.RequireRole("Admin","Recruiter","Employer"));
app.MapGet("/reports",async Task<IResult>(string ids,string? format,int? version,IDbContextFactory<AtsDbContext> factory,ReportService reports,HttpContext context)=>{
    var tokens=ids.Split(',',StringSplitOptions.RemoveEmptyEntries);
    if(tokens.Length==0||tokens.Length>50||tokens.Any(x=>!Guid.TryParse(x,out _))) return Results.BadRequest("Select 1–50 assessments.");
    var selected=tokens.Select(Guid.Parse).Distinct().ToList();
    await using var db=await factory.CreateDbContextAsync();
    if(!await EndpointAccess.Allowed(db,context.User,"Reports.Export")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var uid=context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
    var records=await db.Assessments.Where(x=>selected.Contains(x.Id)).AsNoTracking().ToListAsync();
    if(records.Count!=selected.Count) return Results.NotFound();
    if(Access.Scoped(context.User)){var owner=(await WorkspaceContext.Resolve(db,context.User)).Owner;var name=context.User.Identity!.Name;var workspaceId=await EndpointAccess.WorkspaceId(db,context.User);var profileMode=await ProfileModes.Selected(db,context.User,await WorkspaceContext.Resolve(db,context.User));var own=await db.Assessments.CountAsync(a=>selected.Contains(a.Id)&&(profileMode==null||a.Position!.IsAgencyPosition==profileMode)&&((owner||(a.Position!.AssignedRecruiter==name||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==a.PositionId&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==name))))&&a.Position!.WorkspaceId==workspaceId));if(own!=records.Count)return Results.StatusCode(403);}
    if(records.Any(x=>x.State!="Saved"&&x.OwnerId!=uid&&!context.User.IsInRole("Admin"))) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(version is not null) {
        if(records.Count!=1) return Results.BadRequest("Select one application for an assessment-version report.");
        var historical=await db.AssessmentVersions.AsNoTracking().SingleOrDefaultAsync(x=>x.AssessmentId==records[0].Id&&x.Number==version);
        if(historical is null) return Results.NotFound();
        var a=records[0];a.SnapshotJson=historical.SnapshotJson;a.MatchPercent=historical.MatchPercent;a.MustHaveFit=historical.MustHaveFit;a.CoreRoleFit=historical.CoreRoleFit;a.EvidenceStrength=historical.EvidenceStrength;a.Decision=historical.Decision;a.VersionNumber=historical.Number;
    }
    var html=reports.Html(records);context.Response.Headers.CacheControl="no-store";
    byte[]? pdf=format=="pdf"?await reports.Pdf(html):null;
    AtsService.Audit(db,uid,"Report generated",string.Join(',',selected),$"{format??"html"}; version {version?.ToString()??"latest"}");await db.SaveChangesAsync();
    if(pdf is not null) return Results.File(pdf,"application/pdf","Osynix-Candidate-Reports.pdf");
    return Results.Content(html,"text/html");
}).RequireAuthorization(p=>p.RequireRole("Admin","Recruiter","Employer"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
return 0;
public partial class Program { }
