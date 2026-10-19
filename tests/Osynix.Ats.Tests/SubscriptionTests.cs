using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class SubscriptionTests
{
    sealed class State(string id,string role):AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,id),new(ClaimTypes.Role,role)],"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Fixture:IAsyncDisposable
    {
        readonly SqliteConnection connection=new($"Data Source=subscription-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        public Factory Factory=null!;public RecruitmentUpdates Updates=new();
        public async Task Init(){await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection.ConnectionString).Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);foreach(var role in new[]{"Admin","Employer","Recruiter"})db.Roles.Add(new IdentityRole(role){Id=role});foreach(var id in new[]{"free","other","employer","admin"}){var role=id=="admin"?"Admin":id=="employer"?"Employer":"Recruiter";db.Users.Add(new AtsUser{Id=id,UserName=id,Email=id+"@example.test",EmailConfirmed=true,AccountType=role=="Recruiter"?AccountTypes.Freelance:role=="Employer"?AccountTypes.Employer:AccountTypes.Legacy});db.UserRoles.Add(new IdentityUserRole<string>{UserId=id,RoleId=role});}await db.SaveChangesAsync();}
        public Access Access(string id)=>new(new State(id,id=="admin"?"Admin":id=="employer"?"Employer":"Recruiter"),Factory);
        public EmployerService Employer(string id)=>new(new State(id,id=="admin"?"Admin":id=="employer"?"Employer":"Recruiter"),Access(id),Factory,Updates);
        public SubscriptionService Service(string id)=>new(Employer(id),Access(id),Factory,Updates);
        public ValueTask DisposeAsync()=>connection.DisposeAsync();
    }

    [Fact]public async Task DefaultPlansAreAssignedAndDraftCataloguesRemainPrivate()
    {
        await using var f=new Fixture();await f.Init();var free=await f.Service("free").Overview();
        Assert.Equal("Free",free.Workspace.SubscriptionName);var t=SubscriptionRules.Terms(free.Workspace)!;
        Assert.Equal(1,t.PositionSlots);Assert.Equal(20,t.MonthlyAssessments);Assert.Equal(2,t.MonthlyJdAnalyses);Assert.False(t.Marketplace);Assert.False(await f.Access("free").Can("Reports.Export"));
        Assert.Equal(4,free.Plans.Count);Assert.Single((await f.Service("employer").Overview()).Plans);
        await using var db=f.Factory.CreateDbContext();Assert.Equal(12,await db.SubscriptionPlans.CountAsync());
        var employerDraft=await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Employer"&&p.Code=="growth");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").Request(employerDraft.Id,employerDraft.Revision,"Monthly","PKR",""));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("free").Catalogue());
    }

    [Fact]public async Task ApprovalRequiresPaymentPreservesQuoteAndStartsFreshAllowance()
    {
        await using var f=new Fixture();await f.Init();var current=await f.Service("free").Overview();var growth=current.Plans.Single(p=>p.Code=="growth");
        growth.PricingConfirmed=true;growth.MonthlyPkr=5000;await f.Service("admin").SavePlan(growth,SubscriptionRules.Terms(growth));
        growth=(await f.Service("free").Overview()).Plans.Single(p=>p.Code=="growth");
        await using(var db=f.Factory.CreateDbContext())await SubscriptionRules.Charge(db,current.Workspace.Id,"Assessment");
        await f.Service("free").Request(growth.Id,growth.Revision,"Monthly","PKR","Ready to upgrade");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").Request(growth.Id,growth.Revision,"Monthly","PKR","Duplicate"));
        var changed=(await f.Service("admin").Catalogue()).Single(p=>p.Id==growth.Id);var changedTerms=SubscriptionRules.Terms(changed);changed.MonthlyPkr=7000;changedTerms.PositionSlots=11;await f.Service("admin").SavePlan(changed,changedTerms);
        var request=(await f.Service("free").Overview()).Requests.Single();Assert.Equal(5000,request.QuotedPrice);request.Status="Needs Information";request.AdminResponse="Please provide payment proof.";await f.Employer("admin").Respond(request);
        request=(await f.Service("free").Overview()).Requests.Single();Assert.Equal("Needs Information",request.Status);Assert.False(request.PaymentConfirmed);request.Status="Granted";request.AdminResponse="Payment verified outside ATS.";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Employer("admin").Respond(request));
        Assert.Equal("Free",(await f.Service("free").Overview()).Workspace.SubscriptionName);
        request.PaymentConfirmed=true;await f.Employer("admin").Respond(request);
        var approved=await f.Service("free").Overview();Assert.Equal("Growth",approved.Workspace.SubscriptionName);Assert.Equal(5000,approved.Workspace.SubscriptionPrice);Assert.Equal(10,approved.Workspace.FreePositionSlots);Assert.Equal(0,approved.Assessments);Assert.True(await f.Access("free").Can("Talent.Master"));
        await f.Service("other").Overview();await using(var db=f.Factory.CreateDbContext()){Assert.True(await SubscriptionRules.MarketplaceAllowed(db,"free"));Assert.False(await SubscriptionRules.MarketplaceAllowed(db,"other"));}
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>f.Employer("admin").Respond(request));
        await f.Service("admin").ResetFree(approved.Workspace.Id,approved.Workspace.Revision);
        Assert.False(await f.Access("free").Can("Talent.Master"));Assert.Equal(0,(await f.Service("free").Overview()).Assessments);
    }

    [Fact]public async Task CareersCapReservesAssessmentOnceAndKeepsPositionActive()
    {
        await using var f=new Fixture();await f.Init();var w=(await f.Service("free").Overview()).Workspace;var t=SubscriptionRules.Terms(w)!;t.MonthlyAssessments=2;t.ApplicationsPerPosition=2;
        var job=new Position{WorkspaceId=w.Id,Reference="SUB-01",Title="Recruiter",Status="Active",CareersPublished=true};
        await using(var db=f.Factory.CreateDbContext()){var tracked=await db.EmployerWorkspaces.SingleAsync(x=>x.Id==w.Id);tracked.CareersPublished=true;tracked.SubscriptionTermsJson=JsonSerializer.Serialize(t);db.Positions.Add(job);await db.SaveChangesAsync();}
        var employer=f.Employer("free");
        var first=await employer.ApplyPublic(w.Id,job.Id,"Candidate One","one@example.test","+923001234501","Lahore","cv.txt","Professional CV"u8.ToArray());
        await using(var db=f.Factory.CreateDbContext())await SubscriptionRules.Charge(db,w.Id,"Assessment",first.Id);
        await employer.ApplyPublic(w.Id,job.Id,"Candidate Two","two@example.test","+923001234502","Lahore","cv.txt","Professional CV"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>employer.ApplyPublic(w.Id,job.Id,"Candidate Three","three@example.test","+923001234503","Lahore","cv.txt","Professional CV"u8.ToArray()));
        await using var check=f.Factory.CreateDbContext();var saved=await check.Positions.SingleAsync();Assert.Equal("Active",saved.Status);Assert.False(saved.CareersPublished);Assert.Equal(2,await check.Candidates.CountAsync());Assert.Equal(2,await check.SubscriptionUsage.CountAsync(u=>u.Kind=="Assessment"));Assert.Empty(await employer.PublicJobs(w.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>{await using var db=f.Factory.CreateDbContext();await SubscriptionRules.Charge(db,w.Id,"Assessment");});
    }

    [Fact]public async Task UsageIsMonthlyAndWorkspaceScopedAndStalePricesCannotBeRequested()
    {
        await using var f=new Fixture();await f.Init();var overview=await f.Service("free").Overview();var other=await f.Service("other").Overview();
        await using(var db=f.Factory.CreateDbContext()){var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==overview.Workspace.Id);var terms=SubscriptionRules.Terms(w)!;terms.MonthlyJdAnalyses=1;w.SubscriptionTermsJson=JsonSerializer.Serialize(terms);db.SubscriptionUsage.Add(new(){WorkspaceId=w.Id,Kind="JD",MonthUtc=SubscriptionRules.Month(DateTime.UtcNow).AddMonths(-1)});await db.SaveChangesAsync();}
        await using(var db=f.Factory.CreateDbContext())await SubscriptionRules.Charge(db,overview.Workspace.Id,"JD");
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>{await using var db=f.Factory.CreateDbContext();await SubscriptionRules.Charge(db,overview.Workspace.Id,"JD");});
        await using(var db=f.Factory.CreateDbContext())await SubscriptionRules.Charge(db,other.Workspace.Id,"JD");
        var growth=overview.Plans.Single(p=>p.Code=="growth");growth.MonthlyPkr=1234;await f.Service("admin").SavePlan(growth,SubscriptionRules.Terms(growth));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").Request(growth.Id,growth.Revision,"Annual","USD",""));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("free").ResetFree(other.Workspace.Id,other.Workspace.Revision));
    }

    [Fact]public async Task ConcurrentRequestsCannotSpendTheSameLastAssessment()
    {
        await using var f=new Fixture();await f.Init();var w=(await f.Service("free").Overview()).Workspace;
        await using(var db=f.Factory.CreateDbContext()){var tracked=await db.EmployerWorkspaces.SingleAsync(x=>x.Id==w.Id);var terms=SubscriptionRules.Terms(tracked)!;terms.MonthlyAssessments=1;tracked.SubscriptionTermsJson=JsonSerializer.Serialize(terms);await db.SaveChangesAsync();}
        async Task<bool> Spend(){try{await using var db=f.Factory.CreateDbContext();await SubscriptionRules.Charge(db,w.Id,"Assessment");return true;}catch(InvalidOperationException){return false;}}
        var spent=await Task.WhenAll(Task.Run(Spend),Task.Run(Spend));Assert.Single(spent,x=>x);
        await using var check=f.Factory.CreateDbContext();Assert.Single(await check.SubscriptionUsage.Where(u=>u.WorkspaceId==w.Id&&u.Kind=="Assessment").ToListAsync());
    }
    static async Task<SubscriptionOverview> Activate(Fixture f,string code)
    {
        var current=await f.Service("free").Overview();var plan=current.Plans.Single(p=>p.Code==code);
        await f.Service("free").Request(plan.Id,plan.Revision,"Monthly","PKR","");
        var request=(await f.Service("free").Overview()).Requests.First(r=>r.Status=="Submitted");
        request.Status="Granted";request.PaymentConfirmed=true;request.AdminResponse="Payment confirmed";
        await f.Employer("admin").Respond(request);return await f.Service("free").Overview();
    }

    [Fact]public async Task CancellationRetainsPaidAccessUntilRenewalAndIsReversibleBeforeThen()
    {
        await using var f=new Fixture();await f.Init();var current=await Activate(f,"starter");
        await using(var db=f.Factory.CreateDbContext())await SubscriptionRules.Charge(db,current.Workspace.Id,"Assessment");
        current=await f.Service("free").Overview();var free=current.Plans.Single(p=>p.Code=="free");var allowance=current.Workspace.SubscriptionAllowanceId;
        await f.Service("free").ScheduleChange(free.Id,free.Revision,current.Workspace.Revision,"Monthly","PKR");
        var pending=await f.Service("free").Overview();Assert.Equal("Starter",pending.Workspace.SubscriptionName);Assert.Equal(1,pending.Assessments);Assert.Equal(allowance,pending.Workspace.SubscriptionAllowanceId);
        Assert.Equal("Cancellation",pending.ScheduledChange!.Kind);Assert.Equal(current.Workspace.SubscriptionRenewalDate,pending.ScheduledChange.EffectiveDate);
        await using(var db=f.Factory.CreateDbContext())Assert.False(await SubscriptionLifecycle.ApplyDue(db,pending.Workspace.Id,pending.ScheduledChange.EffectiveDate.AddDays(-1)));
        await f.Service("free").KeepCurrentPlan(pending.ScheduledChange.Id,pending.ScheduledChange.Revision);
        current=await f.Service("free").Overview();Assert.Null(current.ScheduledChange);Assert.Equal("Starter",current.Workspace.SubscriptionName);Assert.Equal(1,current.Assessments);
        await f.Service("free").ScheduleChange(free.Id,free.Revision,current.Workspace.Revision,"Monthly","PKR");
        pending=await f.Service("free").Overview();
        await using(var db=f.Factory.CreateDbContext())Assert.True(await SubscriptionLifecycle.ApplyDue(db,pending.Workspace.Id,pending.ScheduledChange!.EffectiveDate));
        var cancelled=await f.Service("free").Overview();Assert.Equal("Free",cancelled.Workspace.SubscriptionName);Assert.Null(cancelled.Workspace.SubscriptionRenewalDate);Assert.Null(cancelled.ScheduledChange);Assert.Equal(0,cancelled.Assessments);
        await using(var db=f.Factory.CreateDbContext()){
            Assert.False(await SubscriptionRules.MarketplaceAllowed(db,"free"));Assert.Single(await db.SubscriptionUsage.ToListAsync());
            Assert.False(await SubscriptionLifecycle.ApplyDue(db,cancelled.Workspace.Id,pending.ScheduledChange!.EffectiveDate));
        }
    }

    [Fact]public async Task DowngradeAppliesSavedPlanAtRenewalWithFreshAllowanceAndNoLeftoverCredits()
    {
        await using var f=new Fixture();await f.Init();var current=await Activate(f,"growth");
        await using(var db=f.Factory.CreateDbContext()){for(var i=0;i<3;i++)await SubscriptionRules.Charge(db,current.Workspace.Id,"Assessment");await SubscriptionRules.Charge(db,current.Workspace.Id,"JD");}
        current=await f.Service("free").Overview();var starter=current.Plans.Single(p=>p.Code=="starter");
        starter.PricingConfirmed=true;starter.MonthlyPkr=5000;starter.AnnualPkr=50000;await f.Service("admin").SavePlan(starter,SubscriptionRules.Terms(starter));
        starter=(await f.Service("free").Overview()).Plans.Single(p=>p.Code=="starter");
        await f.Service("free").ScheduleChange(starter.Id,starter.Revision,current.Workspace.Revision,"Annual","PKR");
        var pending=await f.Service("free").Overview();Assert.Equal("Growth",pending.Workspace.SubscriptionName);Assert.Equal(3,pending.Assessments);Assert.Equal(1,pending.Jds);
        var originalTerms=SubscriptionRules.Terms(starter);starter.MonthlyPkr=9000;starter.AnnualPkr=90000;var revised=SubscriptionRules.Terms(starter);revised.MonthlyAssessments=90;await f.Service("admin").SavePlan(starter,revised);
        await using(var db=f.Factory.CreateDbContext())Assert.True(await SubscriptionLifecycle.ApplyDue(db,pending.Workspace.Id,pending.ScheduledChange!.EffectiveDate));
        var next=await f.Service("free").Overview();Assert.Equal("Starter",next.Workspace.SubscriptionName);Assert.Equal(0,next.Assessments);Assert.Equal(0,next.Jds);Assert.Equal(originalTerms.MonthlyAssessments,SubscriptionRules.Terms(next.Workspace)!.MonthlyAssessments);
        Assert.Equal(pending.ScheduledChange!.EffectiveDate.AddYears(1),next.Workspace.SubscriptionRenewalDate);Assert.Equal(50000,next.Workspace.SubscriptionPrice);
        await using(var db=f.Factory.CreateDbContext()){
            for(var i=0;i<50;i++)await SubscriptionRules.Charge(db,next.Workspace.Id,"Assessment");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>SubscriptionRules.Charge(db,next.Workspace.Id,"Assessment"));
            Assert.Equal(54,await db.SubscriptionUsage.CountAsync()); // Original records remain; unused credits never become extra quota.
        }
    }

    [Fact]public async Task UpgradeImmediatelyReplacesScheduledCancellationAndStartsNewAllowance()
    {
        await using var f=new Fixture();await f.Init();var current=await Activate(f,"starter");
        await using(var db=f.Factory.CreateDbContext()){await SubscriptionRules.Charge(db,current.Workspace.Id,"Assessment");await SubscriptionRules.Charge(db,current.Workspace.Id,"JD");}
        current=await f.Service("free").Overview();var free=current.Plans.Single(p=>p.Code=="free");
        await f.Service("free").ScheduleChange(free.Id,free.Revision,current.Workspace.Revision,"Monthly","PKR");
        var pending=await f.Service("free").Overview();var next=await Activate(f,"growth");
        Assert.Equal("Growth",next.Workspace.SubscriptionName);Assert.Null(next.ScheduledChange);Assert.Equal(0,next.Assessments);Assert.Equal(0,next.Jds);Assert.NotEqual(current.Workspace.SubscriptionAllowanceId,next.Workspace.SubscriptionAllowanceId);
        await using var checkDb=f.Factory.CreateDbContext();Assert.Equal("Superseded",(await checkDb.SubscriptionChanges.SingleAsync()).Status);Assert.False(await SubscriptionLifecycle.ApplyDue(checkDb,next.Workspace.Id,pending.ScheduledChange!.EffectiveDate));Assert.Equal(2,await checkDb.SubscriptionUsage.CountAsync());
    }

    [Fact]public async Task ScheduledChangesValidateTierOwnershipAndPreventWithdrawnUpgradeApproval()
    {
        await using var f=new Fixture();await f.Init();var current=await Activate(f,"starter");var growth=current.Plans.Single(p=>p.Code=="growth");var free=current.Plans.Single(p=>p.Code=="free");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").ScheduleChange(growth.Id,growth.Revision,current.Workspace.Revision,"Monthly","PKR"));
        await f.Service("free").Request(growth.Id,growth.Revision,"Monthly","PKR","");
        var oldRequest=(await f.Service("free").Overview()).Requests.First(r=>r.Status=="Submitted");
        await f.Service("free").ScheduleChange(free.Id,free.Revision,current.Workspace.Revision,"Monthly","PKR");
        var pending=await f.Service("free").Overview();Assert.Equal("Withdrawn",pending.Requests.Single(r=>r.Id==oldRequest.Id).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("other").KeepCurrentPlan(pending.ScheduledChange!.Id,pending.ScheduledChange.Revision));
        var withdrawn=pending.Requests.Single(r=>r.Id==oldRequest.Id);withdrawn.Status="Granted";withdrawn.PaymentConfirmed=true;withdrawn.AdminResponse="Cannot grant a withdrawn request";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Employer("admin").Respond(withdrawn));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>f.Service("free").KeepCurrentPlan(pending.ScheduledChange!.Id,Guid.NewGuid()));
        Assert.Equal("Starter",(await f.Service("free").Overview()).Workspace.SubscriptionName);
    }
    [Fact]public async Task AdminRenewalAmendmentsMoveTheScheduledChangeAndCannotRemoveItsDate()
    {
        await using var f=new Fixture();await f.Init();var current=await Activate(f,"growth");var starter=current.Plans.Single(p=>p.Code=="starter");
        await f.Service("free").ScheduleChange(starter.Id,starter.Revision,current.Workspace.Revision,"Monthly","PKR");
        var pending=await f.Service("free").Overview();var original=pending.ScheduledChange!.EffectiveDate;
        pending.Workspace.SubscriptionRenewalDate=null;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Employer("admin").SetAccess(pending.Workspace));
        pending=await f.Service("free").Overview();pending.Workspace.SubscriptionRenewalDate=original.AddDays(5);
        await f.Employer("admin").SetAccess(pending.Workspace);
        var adjusted=await f.Service("admin").AdminScheduledChange(pending.Workspace.Id);Assert.Equal(original.AddDays(5),adjusted!.EffectiveDate);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("other").AdminScheduledChange(pending.Workspace.Id));
        await using var check=f.Factory.CreateDbContext();Assert.False(await SubscriptionLifecycle.ApplyDue(check,pending.Workspace.Id,original));Assert.True(await SubscriptionLifecycle.ApplyDue(check,pending.Workspace.Id,adjusted.EffectiveDate));
        Assert.Equal("Starter",(await f.Service("free").Overview()).Workspace.SubscriptionName);
    }
}
