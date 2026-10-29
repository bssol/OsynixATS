using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class AdminDelegationTests
{
    sealed class State(string id,string role,params Claim[] extra):AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id),new Claim(ClaimTypes.Name,id),new Claim(ClaimTypes.Role,role)}.Concat(extra),"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Fixture:IAsyncDisposable
    {
        readonly SqliteConnection connection=new("Data Source=:memory:");
        public Factory Factory=null!;
        public ServiceProvider Provider=null!;
        public EmployerWorkspace Employer=new(){OwnerId="owner",CompanyName="Employer Company"};
        public EmployerWorkspace Freelancer=new(){OwnerId="freelancer",CompanyName="Independent Recruiter",Kind=AccountTypes.Freelance,FreePositionSlots=1,FreeRecruiterSeats=0};
        public EmployerWorkspace Protected=new(){OwnerId="root",CompanyName="Protected Admin"};
        public async Task Init()
        {
            await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);
            var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IDbContextFactory<AtsDbContext>>(Factory);services.AddScoped(_=>Factory.CreateDbContext());
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddIdentityCore<AtsUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AtsDbContext>().AddDefaultTokenProviders();Provider=services.BuildServiceProvider();
            await using var db=Factory.CreateDbContext();await db.Database.EnsureCreatedAsync();
            foreach(var name in new[]{"Admin","SubAdmin","Employer","Recruiter"})db.Roles.Add(new IdentityRole(name){Id=name,NormalizedName=name.ToUpperInvariant()});
            foreach(var (id,role) in new[]{("root","Admin"),("delegate","SubAdmin"),("owner","Employer"),("freelancer","Recruiter")}){
                db.Users.Add(new(){Id=id,UserName=id,NormalizedUserName=id.ToUpperInvariant(),Email=id+"@example.test",NormalizedEmail=(id+"@example.test").ToUpperInvariant(),FullName=id,EmailConfirmed=true,AccountType=role=="Recruiter"?AccountTypes.Freelance:role});
                db.UserRoles.Add(new(){UserId=id,RoleId=role});
            }
            db.EmployerWorkspaces.AddRange(Employer,Freelancer,Protected);await db.SaveChangesAsync();
        }
        public Access Access(string role="SubAdmin",params Claim[] extra)=>new(new State(role=="Admin"?"root":role=="Employer"?"owner":"delegate",role,extra),Factory);
        public AdminService Admin(Access? access=null)=>new(access??Access(),Provider.GetRequiredService<IServiceScopeFactory>(),Factory);
        public EmployerService Workspaces(Access? access=null)=>new(new State("delegate","SubAdmin"),access??Access(),Factory);
        public async Task Grants(params string[] permissions){await using var db=Factory.CreateDbContext();db.UserClaims.RemoveRange(await db.UserClaims.Where(claim=>claim.UserId=="delegate").ToListAsync());foreach(var permission in permissions)db.UserClaims.Add(new(){UserId="delegate",ClaimType=Osynix.Ats.Services.Access.AdminPermissionClaim,ClaimValue=permission});await db.SaveChangesAsync();}
        public async Task<EmployerAccessRequest> Request(){await using var db=Factory.CreateDbContext();var request=new EmployerAccessRequest{WorkspaceId=Employer.Id,Feature="Position Slots",Quantity=2,Remarks="Additional slots"};db.Add(request);await db.SaveChangesAsync();return request;}
        public async ValueTask DisposeAsync(){await Provider.DisposeAsync();await connection.DisposeAsync();}
    }

    [Fact]public async Task WorkspaceViewerSeesOwnersButCannotChangeAccountsRequestsOrEntitlements()
    {
        await using var f=new Fixture();await f.Init();await f.Grants("admin.workspaces.view");
        Assert.NotEmpty(await f.Workspaces().Administration());var owners=await f.Admin().WorkspaceOwners();Assert.Equal(2,owners.Count);Assert.DoesNotContain(owners,owner=>owner.Id is "root" or "delegate");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().SetAccess(f.Employer));
        var request=await f.Request();request.Status="Under Review";request.AdminResponse="Reviewing";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().Respond(request));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().UpdateWorkspaceOwner(f.Employer.Id,"Changed","Inactive"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().ResetWorkspaceOwnerPassword(f.Employer.Id,"Testing1!"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().Users());
    }

    [Fact]public async Task ReviewAndGrantAreSeparatePermissionsAndApprovedCapacityIsAddedOnce()
    {
        await using var f=new Fixture();await f.Init();var request=await f.Request();
        await f.Grants("admin.workspaces.view","admin.requests.review");
        request.Status="Under Review";request.AdminResponse="Review Started";await f.Workspaces().Respond(request);
        await using(var db=f.Factory.CreateDbContext()){request=await db.EmployerAccessRequests.AsNoTracking().SingleAsync();}
        request.Status="Granted";request.AdminResponse="Approved";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().Respond(request));
        await f.Grants("admin.workspaces.view","admin.subscriptions.manage");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().Respond(request));
        await f.Grants("admin.workspaces.view","admin.requests.review","admin.subscriptions.manage");
        await f.Workspaces().Respond(request);
        await using(var db=f.Factory.CreateDbContext()){Assert.Equal(2,(await db.EmployerWorkspaces.FindAsync(f.Employer.Id))!.AdditionalPositionSlots);request=await db.EmployerAccessRequests.AsNoTracking().SingleAsync();}
        await f.Workspaces().Respond(request);
        await using(var db=f.Factory.CreateDbContext()){Assert.Equal(2,(await db.EmployerWorkspaces.FindAsync(f.Employer.Id))!.AdditionalPositionSlots);}
    }

    [Fact]public async Task ExplicitPermissionsAreReadFromDatabaseAndRevocationTakesEffectImmediately()
    {
        await using var f=new Fixture();await f.Init();await f.Grants("admin.subscriptions.manage");var access=f.Access("SubAdmin",new Claim(Osynix.Ats.Services.Access.AdminPermissionClaim,"admin.requests.review"));
        Assert.True(await access.CanAdmin("admin.subscriptions.manage"));Assert.False(await access.CanAdmin("admin.workspaces.view"));Assert.False(await access.CanAdmin("admin.requests.review"));
        var principal=await access.RequireAdminPermission("admin.subscriptions.manage");Assert.False(principal.HasClaim(Osynix.Ats.Services.Access.AdminPermissionClaim,"admin.requests.review"));
        await f.Grants();Assert.False(await access.CanAdmin("admin.subscriptions.manage"));
        await f.Grants("admin.workspaces.view");await using(var db=f.Factory.CreateDbContext()){var user=await db.Users.FindAsync("delegate");user!.Status="Inactive";await db.SaveChangesAsync();}
        Assert.False(await access.CanAdmin("admin.workspaces.view"));Assert.False(await f.Access("Employer").CanAdmin("admin.workspaces.view"));
    }

    [Fact]public async Task AccountManagerCanUpdateOwnerAndResetPasswordButCannotChangeAdminOrEntitlements()
    {
        await using var f=new Fixture();await f.Init();await f.Grants("admin.workspaces.view","admin.accounts.manage");
        await f.Admin().UpdateWorkspaceOwner(f.Employer.Id,"Updated Owner","Inactive");
        await f.Admin().ResetWorkspaceOwnerPassword(f.Employer.Id,"Testing1!");
        await using(var db=f.Factory.CreateDbContext()){var owner=await db.Users.FindAsync("owner");Assert.Equal("Updated Owner",owner!.FullName);Assert.Equal("Inactive",owner.Status);Assert.NotNull(owner.PasswordHash);Assert.NotEmpty(await db.Audit.ToListAsync());Assert.Equal(3,(await db.EmployerWorkspaces.FindAsync(f.Employer.Id))!.FreePositionSlots);}
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().UpdateWorkspaceOwner(f.Protected.Id,"Root","Inactive"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().ResetWorkspaceOwnerPassword(f.Protected.Id,"Testing1!"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().SetAccess(f.Employer));
    }

    [Fact]public async Task MutationPermissionsDoNotReplaceTheRequiredExplicitWorkspaceViewGrant()
    {
        await using var f=new Fixture();await f.Init();await f.Grants("admin.accounts.manage","admin.subscriptions.manage","admin.requests.review");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().UpdateWorkspaceOwner(f.Employer.Id,"Changed","Inactive"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().SetAccess(f.Employer));
        var request=await f.Request();request.Status="Granted";request.AdminResponse="Approved";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().Respond(request));
    }

    [Fact]public async Task SubAdminCannotDelegateRevokedPermissionsEvenWhenCookieStillContainsThem()
    {
        await using var f=new Fixture();await f.Init();await f.Grants("admin.subadmins.view","admin.subadmins.manage","admin.workspaces.view");
        var access=f.Access("SubAdmin",new Claim(Osynix.Ats.Services.Access.AdminPermissionClaim,"admin.subscriptions.manage"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Admin(access).CreateSubAdmin("new@example.test","New Admin","Testing1!","Partial Control",["admin.workspaces.view","admin.subscriptions.manage"]));
        await using var db=f.Factory.CreateDbContext();Assert.False(await db.Users.AnyAsync(user=>user.Email=="new@example.test"));
    }

    [Fact]public async Task WorkspaceDirectorySeparatesInternalSeatsExternalMembersAndOwnedPositionUsage()
    {
        await using var f=new Fixture();await f.Init();await f.Grants("admin.workspaces.view");
        await using(var db=f.Factory.CreateDbContext()){
            db.Users.Add(new(){Id="employee",UserName="employee",Email="employee@example.test",FullName="Internal Member",AccountType=AccountTypes.Employee,Status="Active"});
            db.WorkspaceMemberships.AddRange(new(){WorkspaceId=f.Employer.Id,UserId="employee",CanCreatePositions=true},new(){WorkspaceId=f.Employer.Id,UserId="freelancer"});
            db.EmployerInvitations.Add(new(){WorkspaceId=f.Employer.Id,Email="invite@example.test",ExpiresUtc=DateTime.UtcNow.AddDays(2),ProtectedToken="must-not-be-returned"});
            foreach(var state in new[]{"Active","On Hold","Draft","Closed","Filled"})db.Positions.Add(new(){WorkspaceId=f.Employer.Id,Reference=state,Title=state,Client="Company",Status=state});
            db.Positions.Add(new(){WorkspaceId=f.Freelancer.Id,Reference="own",Title="Own Position",Client="Client",Status="Active"});
            var w=await db.EmployerWorkspaces.FindAsync(f.Employer.Id);w!.LogoContent=[1,2,3];w.CareersCoverContent=[4,5,6];await db.SaveChangesAsync();
        }
        var directory=await f.Workspaces().Administration();var employer=Assert.Single(directory,i=>i.Workspace.Id==f.Employer.Id);
        Assert.Equal(1,employer.ActiveInternalMembers);Assert.Equal(1,employer.ActiveExternalRecruiters);Assert.Equal(1,employer.PendingInvitations);Assert.Equal(2,employer.UsedPositionSlots);
        Assert.Null(employer.Workspace.LogoContent);Assert.Null(employer.Workspace.CareersCoverContent);
        Assert.Equal(1,Assert.Single(directory,i=>i.Workspace.Id==f.Freelancer.Id).UsedPositionSlots);
        var team=await f.Workspaces().AdministrationTeam(f.Employer.Id);Assert.Equal(2,team.Members.Count);Assert.Single(team.Members,m=>m.Kind=="External Recruiter");Assert.Single(team.Invitations);
        var memberships=await f.Workspaces().AdministrationTeam(f.Freelancer.Id);Assert.Equal("Employer Company",Assert.Single(memberships.Members).Name);
        await f.Grants();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspaces().AdministrationTeam(f.Employer.Id));
    }

    [Fact]public async Task AccountActivityRequiresAuditPermissionAndContainsOnlySelectedAccountEvents()
    {
        await using var f=new Fixture();await f.Init();var request=await f.Request();
        await using(var db=f.Factory.CreateDbContext()){
            db.Audit.AddRange(new(){UserId="root",EntityId=f.Employer.Id.ToString(),Action="Employer entitlement updated",Detail="Seats=2; slots=5; agency=True"},
                new(){UserId="root",EntityId=request.Id.ToString(),Action="Employer request responded",Detail="Under Review"},
                new(){UserId="root",EntityId=f.Freelancer.Id.ToString(),Action="Other Account Event",Detail="Private Other Account"});await db.SaveChangesAsync();
        }
        await f.Grants("admin.workspaces.view");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().WorkspaceActivity(f.Employer.Id));
        await f.Grants("admin.workspaces.view","admin.audit.view");var activity=await f.Admin().WorkspaceActivity(f.Employer.Id);
        Assert.Equal(2,activity.Count);Assert.All(activity,e=>Assert.Equal("Employer Company",e.Account));Assert.All(activity,e=>Assert.Equal("root",e.AccountInfo));
        Assert.Contains(activity,e=>e.Details.Contains("Recruiter seats: 2")&&e.Details.Contains("enabled"));Assert.DoesNotContain(activity,e=>e.Action=="Other Account Event");
        await f.Grants("admin.audit.view");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Admin().WorkspaceActivity(f.Employer.Id));
    }

    [Fact]public async Task RootCanAssignWorkspaceDelegationAndViewOnlyCannotReceiveChangePermissions()
    {
        await using var f=new Fixture();await f.Init();var admin=f.Admin(f.Access("Admin"));
        await admin.CreateSubAdmin("viewer@example.test","Workspace Viewer","Testing1!","View Only",["admin.workspaces.view"]);
        await admin.CreateSubAdmin("manager@example.test","Workspace Manager","Testing1!","Full Control",Osynix.Ats.Services.Access.AdminPermissionKeys.ToList());
        Assert.Contains(await admin.SubAdmins(),account=>account.Email=="manager@example.test"&&account.Permissions.Contains("admin.subscriptions.manage"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>admin.CreateSubAdmin("invalid@example.test","Invalid Viewer","Testing1!","View Only",["admin.workspaces.view","admin.accounts.manage"]));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>admin.CreateSubAdmin("noview@example.test","Invalid Manager","Testing1!","Partial Control",["admin.requests.review"]));
        Assert.Equal("/settings",AccountRegistration.Home(["SubAdmin"]));
    }
}
