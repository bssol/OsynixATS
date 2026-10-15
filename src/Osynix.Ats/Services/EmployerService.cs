using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;

public record PublicCareerJob(Guid Id,string Title,string Location,string EmploymentType,string SalaryRange,string Description);
public record EmployerUsage(int ActiveRecruiters,int ActivePositions,int UsedPositionSlots,int Candidates,int UpcomingInterviews);
public record EmployerOverview(EmployerWorkspace Workspace,List<EmployerAccessRequest> Requests);
public class EmployerService(AuthenticationStateProvider auth,Access access,IDbContextFactory<AtsDbContext> factory)
{
    public async Task<AtsUser> RequireAccount()
    {
        var principal=(await auth.GetAuthenticationStateAsync()).User;
        if(principal.Identity?.IsAuthenticated!=true||!principal.IsInRole("Employer"))throw new UnauthorizedAccessException("Employer access is required.");
        await using var db=await factory.CreateDbContextAsync();
        var user=await db.Users.FindAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        if(user is null)throw new UnauthorizedAccessException("Account not found.");
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==user!.Id select r.Name!).ToListAsync();
        if(user is null||user.Status!="Active"||!user.EmailConfirmed||!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order()))throw new UnauthorizedAccessException("Your account access changed. Please sign in again.");
        return user;
    }
    public static async Task<EmployerWorkspace> EnsureWorkspace(AtsDbContext db,AtsUser user){
        var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.OwnerId==user.Id);
        if(workspace is null){workspace=new(){OwnerId=user.Id,CompanyName=string.IsNullOrWhiteSpace(user.FullName)?"My Company":user.FullName};db.EmployerWorkspaces.Add(workspace);AtsService.Audit(db,user.Id,"Employer workspace created",workspace.Id.ToString(),"One free recruiter seat; three shared position slots.");try{await db.SaveChangesAsync();}catch(DbUpdateException){db.ChangeTracker.Clear();workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.OwnerId==user.Id);}}
        return workspace;
    }
    public async Task<EmployerOverview> Overview()
    {
        var user=await RequireAccount();await using var db=await factory.CreateDbContextAsync();
        var workspace=await EnsureWorkspace(db,user);
        var requests=await db.EmployerAccessRequests.AsNoTracking().Where(r=>r.WorkspaceId==workspace.Id).OrderByDescending(r=>r.CreatedUtc).ToListAsync();
        return new(workspace,requests);
    }
    public async Task<EmployerUsage> Usage(){
        var overview=await Overview();await using var db=await factory.CreateDbContextAsync();var id=overview.Workspace.Id;
        var members=await db.Users.CountAsync(u=>u.EmployerWorkspaceId==id&&u.Status=="Active");var positions=await db.Positions.CountAsync(p=>p.WorkspaceId==id&&p.Status=="Active");var slots=await db.Positions.CountAsync(p=>p.WorkspaceId==id&&(p.Status=="Active"||p.Status=="On Hold"));
        var candidates=await db.Assessments.Where(a=>a.Position!.WorkspaceId==id&&a.State=="Saved"&&a.CandidateId!=null).Select(a=>a.CandidateId).Distinct().CountAsync();
        var interviews=await db.Assessments.CountAsync(a=>a.Position!.WorkspaceId==id&&a.State=="Saved"&&a.InterviewStatus=="Scheduled");return new(members,positions,slots,candidates,interviews);
    }
    public async Task SaveCompany(EmployerWorkspace input)
    {
        var user=await RequireAccount();await using var db=await factory.CreateDbContextAsync();
        var w=await db.EmployerWorkspaces.SingleOrDefaultAsync(w=>w.OwnerId==user.Id&&w.Id==input.Id)??throw new UnauthorizedAccessException("Company not found in your workspace.");
        if(w.Revision!=input.Revision)throw new DbUpdateConcurrencyException("Company details changed. Reload before saving.");
        if(input.OperatingType is not("Company Owner" or "Freelancer"))throw new InvalidOperationException("Choose Company Owner or Freelancer.");
        if(string.IsNullOrWhiteSpace(input.CompanyName)||input.CompanyName.Length>200||input.AboutUs.Length>5000||input.BusinessEmail.Length>320||input.Website.Length>500||input.ContactNumber.Length>50||input.Address.Length>500||input.Country.Length>100||input.TimeZone.Length>100)throw new InvalidOperationException("Check the company fields and their maximum lengths.");
        if(input.BusinessEmail.Length>0&&!new EmailAddressAttribute().IsValid(input.BusinessEmail))throw new InvalidOperationException("Enter a valid business email.");
        if(input.Website.Length>0&&(!Uri.TryCreate(input.Website,UriKind.Absolute,out var uri)||uri.Scheme is not("https" or "http")))throw new InvalidOperationException("Website must start with https:// or http://.");
        try{TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone);}catch{throw new InvalidOperationException("Enter a valid time zone, such as Asia/Karachi.");}
        if(input.LogoContent is {Length:>0} bytes){if(bytes.Length>1024*1024)throw new InvalidOperationException("Logo must be at most 1 MB.");var png=bytes.Length>8&&bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10});var jpeg=bytes.Length>3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255;if(!png&&!jpeg)throw new InvalidOperationException("Upload a PNG or JPEG logo.");input.LogoContentType=png?"image/png":"image/jpeg";}
        if(!CareersBranding.Palettes.Any(p=>p.Key==input.CareersColorScheme)||input.CareersCoverMode is not("Stock" or "Upload" or "None")||!CareersBranding.Covers.Any(c=>c.Id==input.CareersStockCover))throw new InvalidOperationException("Choose An Available Careers Colour Scheme And Cover.");
        var coverType=input.CareersCoverContent is {Length:>0}?CareersBranding.ValidateCover(input.CareersCoverContent):"";
        if(input.CareersCoverMode=="Upload"&&coverType.Length==0)throw new InvalidOperationException("Upload A Cover Photo Before Selecting Your Own Cover.");
        w.CareersColorScheme=input.CareersColorScheme;w.CareersCoverMode=input.CareersCoverMode;w.CareersStockCover=input.CareersStockCover;w.CareersCoverContent=input.CareersCoverContent;w.CareersCoverContentType=coverType;
        w.OperatingType=input.OperatingType;w.CompanyName=input.CompanyName.Trim();w.AboutUs=input.AboutUs.Trim();w.Website=input.Website.Trim();w.BusinessEmail=input.BusinessEmail.Trim();w.ContactNumber=input.ContactNumber.Trim();w.Address=input.Address.Trim();w.Country=input.Country.Trim();w.TimeZone=input.TimeZone;w.LogoContent=input.LogoContent;w.LogoContentType=input.LogoContent is null?"":input.LogoContentType;w.ShowBusinessContacts=input.ShowBusinessContacts;w.CareersPublished=input.CareersPublished;
        AtsService.Audit(db,user.Id,"Employer company updated",w.Id.ToString(),$"Careers published={w.CareersPublished}; public contacts={w.ShowBusinessContacts}");await db.SaveChangesAsync();
    }
    public async Task Request(string feature,int quantity,string remarks)
    {
        var overview=await Overview();var user=await RequireAccount();
        if(feature is not("Recruiter Seats" or "Position Slots" or "Team Talent Pool")||quantity<1||quantity>1000||remarks.Length>2000)throw new InvalidOperationException("Choose a valid feature and quantity (1–1000).");
        await using var db=await factory.CreateDbContextAsync();var request=new EmployerAccessRequest{WorkspaceId=overview.Workspace.Id,Feature=feature,Quantity=feature=="Team Talent Pool"?1:quantity,Remarks=remarks.Trim()};db.EmployerAccessRequests.Add(request);AtsService.Audit(db,user.Id,"Employer access requested",request.Id.ToString(),$"{feature}; quantity={request.Quantity}");await db.SaveChangesAsync();
    }
    public async Task<List<EmployerOverview>> Administration()
    {
        await access.Require(true);await using var db=await factory.CreateDbContextAsync();var workspaces=await db.EmployerWorkspaces.AsNoTracking().OrderBy(w=>w.CompanyName).ToListAsync();var requests=await db.EmployerAccessRequests.AsNoTracking().OrderByDescending(r=>r.CreatedUtc).ToListAsync();return workspaces.Select(w=>new EmployerOverview(w,requests.Where(r=>r.WorkspaceId==w.Id).ToList())).ToList();
    }
    public async Task SetAccess(EmployerWorkspace input)
    {
        var admin=await access.Require(true);if(new[]{input.FreeRecruiterSeats,input.AdditionalRecruiterSeats,input.FreePositionSlots,input.AdditionalPositionSlots}.Any(n=>n<0||n>1000))throw new InvalidOperationException("Limits must be between 0 and 1000.");
        await using var db=await factory.CreateDbContextAsync();var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==input.Id);if(w.Revision!=input.Revision)throw new DbUpdateConcurrencyException("Access changed. Reload before saving.");
        w.FreeRecruiterSeats=input.FreeRecruiterSeats;w.AdditionalRecruiterSeats=input.AdditionalRecruiterSeats;w.FreePositionSlots=input.FreePositionSlots;w.AdditionalPositionSlots=input.AdditionalPositionSlots;w.TeamTalentPoolEnabled=input.TeamTalentPoolEnabled;
        AtsService.Audit(db,admin.FindFirstValue(ClaimTypes.NameIdentifier)!,"Employer entitlement updated",w.Id.ToString(),$"Seats={w.FreeRecruiterSeats+w.AdditionalRecruiterSeats}; slots={w.FreePositionSlots+w.AdditionalPositionSlots}; team talent={w.TeamTalentPoolEnabled}");await db.SaveChangesAsync();
    }
    public async Task Respond(EmployerAccessRequest input)
    {
        var admin=await access.Require(true);if(input.Status is not("Under Review" or "Needs Information" or "Granted" or "Rejected")||string.IsNullOrWhiteSpace(input.AdminResponse)||input.AdminResponse.Length>2000)throw new InvalidOperationException("Choose a response status and enter an Admin response.");
        await using var db=await factory.CreateDbContextAsync();var r=await db.EmployerAccessRequests.SingleAsync(r=>r.Id==input.Id);if(r.Revision!=input.Revision)throw new DbUpdateConcurrencyException("Request changed. Reload before responding.");
        var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==r.WorkspaceId);
        if(input.Status=="Granted"&&r.Status!="Granted"){if(r.Feature=="Recruiter Seats")w.AdditionalRecruiterSeats+=r.Quantity;else if(r.Feature=="Position Slots")w.AdditionalPositionSlots+=r.Quantity;else w.TeamTalentPoolEnabled=true;}
        if(r.Status=="Granted"&&input.Status!="Granted")throw new InvalidOperationException("Use Workspace Access controls to revoke a grant; preserve the approval history.");
        r.Status=input.Status;r.AdminResponse=input.AdminResponse.Trim();r.RespondedUtc=DateTime.UtcNow;AtsService.Audit(db,admin.FindFirstValue(ClaimTypes.NameIdentifier)!,"Employer request responded",r.Id.ToString(),r.Status);await db.SaveChangesAsync();
    }
    public async Task<List<PublicCareerJob>> PublicJobs(Guid workspaceId)
    {
        if(await PublicCompany(workspaceId) is null)return [];
        await using var db=await factory.CreateDbContextAsync();
        return await db.Positions.AsNoTracking().Where(p=>p.WorkspaceId==workspaceId&&p.CareersPublished&&p.Status=="Active")
            .OrderByDescending(p=>p.CreatedUtc).Select(p=>new PublicCareerJob(p.Id,p.Title,p.Location,p.EmploymentType,p.SalaryRange,p.PublicJobDescription)).ToListAsync();
    }
    public async Task<EmployerWorkspace?> PublicCompany(Guid id)
    {
        await using var db=await factory.CreateDbContextAsync();return await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.Id==id&&w.CareersPublished&&db.Users.Any(u=>u.Id==w.OwnerId&&u.Status=="Active"&&u.EmailConfirmed)&&db.UserRoles.Any(ur=>ur.UserId==w.OwnerId&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Employer")));
    }
}
