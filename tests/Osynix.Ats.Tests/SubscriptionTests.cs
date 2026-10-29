using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class SubscriptionTests
{
    [Theory]
    [InlineData("free")]
    [InlineData("starter")]
    [InlineData("growth")]
    [InlineData("pro")]
    public async Task EmployerCareersLimitsSaveAfterMigratedRevisionRepair(string code)
    {
        await using var f=new Fixture();await f.Init();
        if(code=="free")await f.Service("employer").Overview();else await ActivateEmployer(f,code);
        await using(var db=f.Factory.CreateDbContext()){
            await db.GetService<IMigrator>().MigrateAsync("20261025200000_EmployerSubscriptionCatalogue");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SubscriptionPlans SET Revision=lower(Revision) WHERE AccountCategory='Employer' AND Code={code}");
            await db.Database.MigrateAsync();
        }
        var plan=(await f.Service("admin").Catalogue()).Single(p=>p.AccountCategory=="Employer"&&p.Code==code);
        var terms=SubscriptionRules.Terms(plan);terms.ApplicationsPerPosition=17;
        await f.Service("admin").SavePlan(plan,terms,true);
        var saved=(await f.Service("admin").Catalogue()).Single(p=>p.Id==plan.Id);
        Assert.Equal(17,SubscriptionRules.Terms(saved).ApplicationsPerPosition);
        Assert.Equal(17,SubscriptionRules.Terms((await f.Service("employer").Overview()).Workspace)!.ApplicationsPerPosition);
        terms.ApplicationsPerPosition=23;await f.Service("admin").SavePlan(saved,terms,true);
        Assert.Equal(23,SubscriptionRules.Terms((await f.Service("employer").Overview()).Workspace)!.ApplicationsPerPosition);
    }
    [Fact]
    public async Task UpgradeRequestCreatesPaymentInboxAndOwnerCanWithdraw()
    {
        await using var f=new Fixture();await f.Init();
        var overview=await f.Service("free").Overview();var plan=overview.Plans.Single(p=>p.AccountCategory=="Freelancer"&&p.Code=="starter");
        plan.PricingConfirmed=true;plan.MonthlyPkr=8000;await f.Service("admin").SavePlan(plan,SubscriptionRules.Terms(plan));
        plan=(await f.Service("free").Overview()).Plans.Single(p=>p.Id==plan.Id);
        await f.Service("free").Request(plan.Id,plan.Revision,"Monthly","PKR","Please upgrade my plan.");
        Guid requestId;
        await using(var db=f.Factory.CreateDbContext()){
            var request=await db.EmployerAccessRequests.SingleAsync(r=>r.WorkspaceId==overview.Workspace.Id&&r.SubscriptionPlanId!=null);requestId=request.Id;
            var conversation=await db.SupportConversations.SingleAsync(c=>c.EmployerAccessRequestId==request.Id);
            var message=await db.SupportMessages.SingleAsync(m=>m.ConversationId==conversation.Id);
            Assert.Contains("PKR",message.Body);Assert.Contains("Test Bank",message.Body);Assert.Contains("24 hours",message.Body);Assert.Equal("Subscription & Payment",conversation.IssueType);
        }
        await f.Service("free").WithdrawUpgrade(requestId);
        await using(var db=f.Factory.CreateDbContext()){
            Assert.Equal("Withdrawn",(await db.EmployerAccessRequests.SingleAsync(r=>r.Id==requestId)).Status);
            var conversation=await db.SupportConversations.SingleAsync(c=>c.EmployerAccessRequestId==requestId);
            Assert.Equal("Open",conversation.Status);
            Assert.Contains(await db.SupportMessages.Where(m=>m.ConversationId==conversation.Id).Select(m=>m.Body).ToListAsync(),m=>m.Contains("withdrawn",StringComparison.OrdinalIgnoreCase));
        }
    }
    sealed class State(string id,string role):AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,id),new(ClaimTypes.Role,role)],"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    sealed class Fixture:IAsyncDisposable
    {
        readonly SqliteConnection connection=new($"Data Source=subscription-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        public Factory Factory=null!;public RecruitmentUpdates Updates=new();
        public async Task Init(){await connection.OpenAsync();Factory=new(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection.ConnectionString).Options);await using var db=Factory.CreateDbContext();await DatabaseInitializer.InitializeAsync(db);foreach(var role in new[]{"Admin","Employer","Recruiter"})db.Roles.Add(new IdentityRole(role){Id=role});foreach(var id in new[]{"free","other","employer","admin"}){var role=id=="admin"?"Admin":id=="employer"?"Employer":"Recruiter";db.Users.Add(new AtsUser{Id=id,UserName=id,Email=id+"@example.test",EmailConfirmed=true,AccountType=role=="Recruiter"?AccountTypes.Freelance:role=="Employer"?AccountTypes.Employer:AccountTypes.Legacy});db.UserRoles.Add(new IdentityUserRole<string>{UserId=id,RoleId=role});}await db.SaveChangesAsync();await using var settingsDb=Factory.CreateDbContext();settingsDb.AdminPaymentDetails.Add(new AdminPaymentDetails{Id=AdminPaymentDetailsService.SettingsId,BankName="Test Bank",AccountTitle="Osynix Group",AccountNumber="1234567890",Iban="PK00TEST0000000000000000"});await settingsDb.SaveChangesAsync();}
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
        Assert.Equal(4,free.Plans.Count);Assert.Equal(4,(await f.Service("employer").Overview()).Plans.Count);
        await using var db=f.Factory.CreateDbContext();Assert.Equal(12,await db.SubscriptionPlans.CountAsync());
        var employerDraft=await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Employer"&&p.Code=="growth");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service("free").Request(employerDraft.Id,employerDraft.Revision,"Monthly","PKR",""));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("free").Catalogue());
    }

    [Fact]public async Task ApprovedEmployerPlansHaveIndependentLimitsAndFreeFeatureGates()
    {
        await using var f=new Fixture();await f.Init();var current=await f.Service("employer").Overview();
        var free=SubscriptionRules.Terms(current.Workspace)!;
        Assert.Equal(3,free.PositionSlots);Assert.Equal(0,free.InternalSeats);Assert.Equal(0,current.Workspace.AdditionalRecruiterSeats);
        Assert.Equal(30,free.MonthlyAssessments);Assert.Equal(3,free.MonthlyJdAnalyses);
        Assert.False(await f.Access("employer").Can("HiringReview.Share"));Assert.False(await f.Access("employer").Can("Freelancers.View"));
        Assert.False(await f.Access("employer").Can("Clients.Create"));Assert.False(await f.Access("employer").Can("Talent.Master"));Assert.False(await f.Access("employer").Can("EmailTemplates.Manage"));
        var starter=current.Plans.Single(p=>p.Code=="starter");var st=SubscriptionRules.Terms(starter);
        Assert.Equal(7500,starter.MonthlyPkr);Assert.Equal(75000,starter.AnnualPkr);Assert.Equal(25,starter.MonthlyUsd);Assert.Equal(250,starter.AnnualUsd);
        Assert.Equal(5,st.PositionSlots);Assert.Equal(1,st.InternalSeats);Assert.Equal(5,st.MonthlyFreelancerApproaches);Assert.True(st.HiringManagerReviews);Assert.True(st.FreelancerDirectory);Assert.False(st.MeetingIntegrations);
        var growth=SubscriptionRules.Terms(current.Plans.Single(p=>p.Code=="growth"));Assert.Equal(10,growth.PositionSlots);Assert.Equal(3,growth.InternalSeats);Assert.Equal(500,growth.MonthlyAssessments);Assert.Equal(30,growth.MonthlyJdAnalyses);Assert.Null(growth.MonthlyFreelancerApproaches);Assert.True(growth.MasterPool);Assert.True(growth.EmailTemplates);Assert.False(growth.ZoomIntegration);
        var pro=SubscriptionRules.Terms(current.Plans.Single(p=>p.Code=="pro"));Assert.Equal(20,pro.PositionSlots);Assert.Equal(5,pro.InternalSeats);Assert.Equal(1500,pro.MonthlyAssessments);Assert.Equal(100,pro.MonthlyJdAnalyses);Assert.True(pro.ZoomIntegration);
        await using var db=f.Factory.CreateDbContext();Assert.Single(await db.SubscriptionPlans.Where(p=>p.AccountCategory=="Agency"&&p.Published).ToListAsync());
    }

    static async Task<SubscriptionOverview> ActivateEmployer(Fixture f,string code)
    {
        var before=await f.Service("employer").Overview();var plan=before.Plans.Single(p=>p.Code==code);
        await f.Service("employer").Request(plan.Id,plan.Revision,"Monthly","PKR","");
        var request=(await f.Service("employer").Overview()).Requests.Single(r=>r.SubscriptionPlanId==plan.Id);
        request.Status="Granted";request.PaymentConfirmed=true;request.AdminResponse="Payment confirmed outside ATS.";
        await f.Employer("admin").Respond(request);return await f.Service("employer").Overview();
    }

    [Fact]public async Task EmployerStarterCountsUniqueOutreachAndBlocksTheSixthMember()
    {
        await using var f=new Fixture();await f.Init();var current=await ActivateEmployer(f,"starter");
        Assert.True(await f.Access("employer").Can("HiringReview.Share"));Assert.True(await f.Access("employer").Can("Freelancers.View"));Assert.False(await f.Access("employer").Can("Clients.Create"));
        await using var db=f.Factory.CreateDbContext();
        for(var i=0;i<5;i++)await SubscriptionRules.ChargeFreelancerApproach(db,current.Workspace.Id,$"member-{i}");
        await SubscriptionRules.ChargeFreelancerApproach(db,current.Workspace.Id,"member-0");
        Assert.Equal(5,await SubscriptionRules.FreelancerApproaches(db,current.Workspace.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>SubscriptionRules.ChargeFreelancerApproach(db,current.Workspace.Id,"member-5"));
        Assert.Equal(5,(await f.Service("employer").Overview()).FreelancerApproaches);
        var growth=await ActivateEmployer(f,"growth");
        for(var i=5;i<8;i++)await SubscriptionRules.ChargeFreelancerApproach(db,growth.Workspace.Id,$"member-{i}");
        Assert.Equal(8,await SubscriptionRules.FreelancerApproaches(db,growth.Workspace.Id));
        foreach(var usage in await db.SubscriptionUsage.Where(u=>u.Kind.StartsWith("FreelancerApproach:")).ToListAsync())usage.MonthUtc=SubscriptionRules.Month(DateTime.UtcNow).AddMonths(-1);
        await db.SaveChangesAsync();Assert.Equal(0,await SubscriptionRules.FreelancerApproaches(db,current.Workspace.Id));
    }

    [Fact]public async Task AdminCanApplyEmployerLimitsWithoutResettingBillingOrRecordedUsage()
    {
        await using var f=new Fixture();await f.Init();var current=await ActivateEmployer(f,"starter");
        var allowance=current.Workspace.SubscriptionAllowanceId;var renewal=current.Workspace.SubscriptionRenewalDate;
        await using(var db=f.Factory.CreateDbContext())await SubscriptionRules.Charge(db,current.Workspace.Id,"JD");
        var plan=(await f.Service("admin").Catalogue()).Single(p=>p.AccountCategory=="Employer"&&p.Code=="starter");var terms=SubscriptionRules.Terms(plan);terms.PositionSlots=7;plan.MonthlyPkr=9000;
        await f.Service("admin").SavePlan(plan,terms);
        Assert.Equal(5,(await f.Service("employer").Overview()).Workspace.FreePositionSlots);
        plan=(await f.Service("admin").Catalogue()).Single(p=>p.Id==plan.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service("employer").SavePlan(plan,terms,true));
        await f.Service("admin").SavePlan(plan,terms,true);
        var updated=await f.Service("employer").Overview();Assert.Equal(7,updated.Workspace.FreePositionSlots);Assert.Equal(7500,updated.Workspace.SubscriptionPrice);Assert.Equal(renewal,updated.Workspace.SubscriptionRenewalDate);Assert.Equal(allowance,updated.Workspace.SubscriptionAllowanceId);Assert.Equal(1,updated.Jds);
        Assert.Equal(1,(await f.Service("free").Overview()).Workspace.FreePositionSlots);
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
