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
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath,"App_Data"));
var sqlite=new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Ats"));
if(sqlite.DataSource!=":memory:" && !Path.IsPathRooted(sqlite.DataSource)) sqlite.DataSource=Path.GetFullPath(sqlite.DataSource,builder.Environment.ContentRootPath);
if(sqlite.DataSource!=":memory:") Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
builder.Services.AddDbContextFactory<AtsDbContext>(o=>o.UseSqlite(sqlite.ConnectionString));
builder.Services.AddIdentity<AtsUser,IdentityRole>(o=>{
    o.Password.RequiredLength=12; o.User.RequireUniqueEmail=true;
    o.Lockout.MaxFailedAccessAttempts=5; o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AtsDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o=>{o.LoginPath="/Account/Login";o.AccessDeniedPath="/denied";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Lax;o.ExpireTimeSpan=TimeSpan.FromHours(8);o.SlidingExpiration=true;});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddRazorPages();
builder.Services.AddScoped<AuthenticationStateProvider,RevalidatingIdentityStateProvider>();
builder.Services.AddScoped<AdminService>(); builder.Services.AddScoped<Access>(); builder.Services.AddScoped<AtsService>(); builder.Services.AddScoped<WorkbookImport>(); builder.Services.AddScoped<ReportService>();
builder.Services.AddHttpClient<AiService>(c=>c.Timeout=TimeSpan.FromMinutes(4));
var app=builder.Build();
using(var scope=app.Services.CreateScope()) {
    var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();
    // Initial SQLite bootstrap. See docs/database-evolution.md before changing a deployed schema.
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach(var role in new[]{"Admin","Recruiter"}) if(!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new(role));
    var users=scope.ServiceProvider.GetRequiredService<UserManager<AtsUser>>();
    var email=builder.Configuration["Bootstrap:Email"]; var password=builder.Configuration["Bootstrap:Password"];
    if(!await users.Users.AnyAsync()&&!string.IsNullOrWhiteSpace(email)&&!string.IsNullOrWhiteSpace(password)) {
        var user=new AtsUser{UserName=email,Email=email,EmailConfirmed=true}; var result=await users.CreateAsync(user,password);
        if(!result.Succeeded) throw new InvalidOperationException("Administrator bootstrap failed: "+string.Join("; ",result.Errors.Select(e=>e.Description)));
        await users.AddToRoleAsync(user,"Admin");
    }
}
if(!app.Environment.IsDevelopment()) {app.UseExceptionHandler("/error");app.UseHsts();app.UseHttpsRedirection();}
app.UseStaticFiles();app.UseAuthentication();app.UseAuthorization();app.UseAntiforgery();
app.MapRazorPages();
app.MapGet("/files/{id:guid}",async Task<IResult>(Guid id,IDbContextFactory<AtsDbContext> factory,HttpContext context)=>{
    await using var db=await factory.CreateDbContextAsync(); var doc=await db.Documents.FindAsync(id);
    context.Response.Headers.CacheControl="no-store";
    return doc is null?Results.NotFound():Results.File(doc.Content,doc.ContentType,doc.FileName);
}).RequireAuthorization(p=>p.RequireRole("Admin","Recruiter"));
app.MapGet("/reports",async Task<IResult>(string ids,string? format,IDbContextFactory<AtsDbContext> factory,ReportService reports,HttpContext context)=>{
    var tokens=ids.Split(',',StringSplitOptions.RemoveEmptyEntries);
    if(tokens.Length==0||tokens.Length>50||tokens.Any(x=>!Guid.TryParse(x,out _))) return Results.BadRequest("Select 1–50 assessments.");
    var selected=tokens.Select(Guid.Parse).Distinct().ToList();
    await using var db=await factory.CreateDbContextAsync(); var records=await db.Assessments.Where(x=>selected.Contains(x.Id)).AsNoTracking().ToListAsync();
    if(records.Count!=selected.Count) return Results.NotFound();
    var html=reports.Html(records);context.Response.Headers.CacheControl="no-store";
    if(format=="pdf") return Results.File(await reports.Pdf(html),"application/pdf","Osynix-Candidate-Reports.pdf");
    return Results.Content(html,"text/html");
}).RequireAuthorization(p=>p.RequireRole("Admin","Recruiter"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
return 0;
public partial class Program { }
