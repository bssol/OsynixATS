using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;

public record PublicCareerJob(Guid Id,string Title,string Location,string EmploymentType,string SalaryRange,string Description,string Department="",string WorkArrangement="");
public record PublicCareerApplication(Guid Id,bool ProfilePending=false);
public record EmployerUsage(int ActiveRecruiters,int ActivePositions,int UsedPositionSlots,int Candidates,int UpcomingInterviews);
public record EmployerOverview(EmployerWorkspace Workspace,List<EmployerAccessRequest> Requests);
public class EmployerService(AuthenticationStateProvider auth,Access access,IDbContextFactory<AtsDbContext> factory,RecruitmentUpdates? updates=null,ICvProfileExtractionQueue? cvProfiles=null)
{
    public async Task<AtsUser> RequireAccount()
    {
        var principal=(await auth.GetAuthenticationStateAsync()).User;
        if(principal.Identity?.IsAuthenticated!=true||!(principal.IsInRole("Employer")||principal.IsInRole("Recruiter")))throw new UnauthorizedAccessException("Employer access is required.");
        await using var db=await factory.CreateDbContextAsync();
        var user=await db.Users.FindAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        if(user is null)throw new UnauthorizedAccessException("Account not found.");
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==user!.Id select r.Name!).ToListAsync();
        if(user is null||user.Status!="Active"||!user.EmailConfirmed||!roles.Order().SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(x=>x.Value).Order()))throw new UnauthorizedAccessException("Your account access changed. Please sign in again.");
        var active=await WorkspaceContext.Resolve(db,principal);if(!active.Owner)throw new UnauthorizedAccessException("Only The Workspace Owner Can Manage These Settings.");
        return user;
    }
    public static async Task<EmployerWorkspace> EnsureWorkspace(AtsDbContext db,AtsUser user){
        var workspace=await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.OwnerId==user.Id);
        if(workspace is null){workspace=new(){OwnerId=user.Id,Kind=user.AccountType==AccountTypes.Freelance?AccountTypes.Freelance:"Employer",FreePositionSlots=user.AccountType==AccountTypes.Freelance?1:3,FreeRecruiterSeats=user.AccountType==AccountTypes.Freelance?0:1,OperatingType=user.AccountType==AccountTypes.Freelance?"Freelancer":"Company Owner",CompanyName=string.IsNullOrWhiteSpace(user.FullName)?"My Company":user.FullName};db.EmployerWorkspaces.Add(workspace);AtsService.Audit(db,user.Id,"Employer workspace created",workspace.Id.ToString(),"One free recruiter seat; three shared position slots.");try{await db.SaveChangesAsync();}catch(DbUpdateException){db.ChangeTracker.Clear();workspace=await db.EmployerWorkspaces.AsNoTracking().SingleAsync(w=>w.OwnerId==user.Id);}}
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
        var members=await db.WorkspaceMemberships.CountAsync(m=>m.WorkspaceId==id&&m.Status=="Active"&&db.Users.Any(u=>u.Id==m.UserId&&u.Status=="Active"&&u.AccountType!=AccountTypes.Freelance&&u.AccountType!=AccountTypes.Employer));var positions=await db.Positions.CountAsync(p=>p.WorkspaceId==id&&p.Status=="Active");var slots=await db.Positions.CountAsync(p=>p.WorkspaceId==id&&(p.Status=="Active"||p.Status=="On Hold"));
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
        AtsService.Audit(db,user.Id,"Employer company updated",w.Id.ToString(),$"Careers published={w.CareersPublished}; public contacts={w.ShowBusinessContacts}");await db.SaveChangesAsync();updates?.Publish();
    }
    public async Task Request(string feature,int quantity,string remarks)
    {
        var overview=await Overview();var user=await RequireAccount();
        if(feature is not("Recruiter Seats" or "Position Slots" or "Team Talent Pool" or "Master Talent Pool" or "Employer Account" or "Agency Account")||quantity<1||quantity>1000||remarks.Length>2000)throw new InvalidOperationException("Choose a valid feature and quantity (1–1000).");
        if(overview.Workspace.Kind==AccountTypes.Freelance&&feature=="Recruiter Seats")throw new InvalidOperationException("Upgrade To An Employer Account Before Adding Team Members.");
        if(feature=="Agency Account"&&(overview.Workspace.Kind!=AccountTypes.Employer||overview.Workspace.AgencyEnabled))throw new InvalidOperationException("Agency Upgrade Is Available Only To Employers Without Active Agency Access.");
        await using var db=await factory.CreateDbContextAsync();if(feature=="Agency Account"&&await db.EmployerAccessRequests.AnyAsync(r=>r.WorkspaceId==overview.Workspace.Id&&r.Feature==feature&&(r.Status=="Submitted"||r.Status=="Under Review"||r.Status=="Needs Information")))throw new InvalidOperationException("An Agency Upgrade Request Is Already Pending.");var request=new EmployerAccessRequest{WorkspaceId=overview.Workspace.Id,Feature=feature,Quantity=feature is "Team Talent Pool" or "Master Talent Pool" or "Employer Account" or "Agency Account"?1:quantity,Remarks=remarks.Trim()};db.EmployerAccessRequests.Add(request);AtsService.Audit(db,user.Id,"Employer access requested",request.Id.ToString(),$"{feature}; quantity={request.Quantity}");await db.SaveChangesAsync();
    }
    public async Task<List<EmployerOverview>> Administration()
    {
        await access.Require(true);await using var db=await factory.CreateDbContextAsync();var workspaces=await db.EmployerWorkspaces.AsNoTracking().OrderBy(w=>w.CompanyName).ToListAsync();var requests=await db.EmployerAccessRequests.AsNoTracking().OrderByDescending(r=>r.CreatedUtc).ToListAsync();return workspaces.Select(w=>new EmployerOverview(w,requests.Where(r=>r.WorkspaceId==w.Id).ToList())).ToList();
    }
    public async Task SetAccess(EmployerWorkspace input)
    {
        var admin=await access.Require(true);if(new[]{input.FreeRecruiterSeats,input.AdditionalRecruiterSeats,input.FreePositionSlots,input.AdditionalPositionSlots}.Any(n=>n<0||n>1000))throw new InvalidOperationException("Limits must be between 0 and 1000.");
        await using var db=await factory.CreateDbContextAsync();var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==input.Id);if(w.Revision!=input.Revision)throw new DbUpdateConcurrencyException("Access changed. Reload before saving.");
        if(w.Kind==AccountTypes.Freelance&&(input.FreeRecruiterSeats!=0||input.AdditionalRecruiterSeats!=0))throw new InvalidOperationException("Upgrade This Account To Employer Before Granting Team Seats.");
        w.FreeRecruiterSeats=input.FreeRecruiterSeats;w.AdditionalRecruiterSeats=input.AdditionalRecruiterSeats;w.FreePositionSlots=input.FreePositionSlots;w.AdditionalPositionSlots=input.AdditionalPositionSlots;w.TeamTalentPoolEnabled=input.TeamTalentPoolEnabled;w.MasterTalentPoolEnabled=input.MasterTalentPoolEnabled;if(input.AgencyEnabled&&w.Kind!=AccountTypes.Employer)throw new InvalidOperationException("Only Employer Workspaces Can Receive Agency Access.");w.AgencyEnabled=input.AgencyEnabled;
        if(await db.Users.AnyAsync(u=>u.Id==w.OwnerId&&u.AccountType!=AccountTypes.Legacy))w.TeamTalentPoolEnabled=w.MasterTalentPoolEnabled;
        AtsService.Audit(db,admin.FindFirstValue(ClaimTypes.NameIdentifier)!,"Employer entitlement updated",w.Id.ToString(),$"Seats={w.FreeRecruiterSeats+w.AdditionalRecruiterSeats}; slots={w.FreePositionSlots+w.AdditionalPositionSlots}; team talent={w.TeamTalentPoolEnabled}; agency={w.AgencyEnabled}");await db.SaveChangesAsync();updates?.Publish();
    }
    public async Task Respond(EmployerAccessRequest input)
    {
        var admin=await access.Require(true);if(input.Status is not("Under Review" or "Needs Information" or "Granted" or "Rejected")||string.IsNullOrWhiteSpace(input.AdminResponse)||input.AdminResponse.Length>2000)throw new InvalidOperationException("Choose a response status and enter an Admin response.");
        await using var db=await factory.CreateDbContextAsync();var r=await db.EmployerAccessRequests.SingleAsync(r=>r.Id==input.Id);if(r.Revision!=input.Revision)throw new DbUpdateConcurrencyException("Request changed. Reload before responding.");
        var w=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==r.WorkspaceId);
        if(input.Status=="Granted"&&r.Feature=="Recruiter Seats"&&w.Kind==AccountTypes.Freelance)throw new InvalidOperationException("Approve The Employer Account Upgrade Before Adding Seats.");
        if(input.Status=="Granted"&&r.Feature=="Agency Account"&&(w.Kind!=AccountTypes.Employer||!await WorkspaceContext.OwnerActive(db,w.OwnerId)))throw new InvalidOperationException("Agency Access Requires An Active Employer Workspace.");
        if(input.Status=="Granted"&&r.Status!="Granted"){if(r.Feature=="Recruiter Seats")w.AdditionalRecruiterSeats+=r.Quantity;else if(r.Feature=="Position Slots")w.AdditionalPositionSlots+=r.Quantity;else if(r.Feature=="Employer Account"){
            w.Kind="Employer";w.FreeRecruiterSeats=Math.Max(1,w.FreeRecruiterSeats);w.FreePositionSlots=Math.Max(3,w.FreePositionSlots);
            var owner=await db.Users.SingleAsync(u=>u.Id==w.OwnerId);owner.AccountType=AccountTypes.Employer;owner.SecurityStamp=Guid.NewGuid().ToString();
            var recruiterRole=await db.Roles.SingleAsync(x=>x.Name=="Recruiter");var employerRole=await db.Roles.SingleAsync(x=>x.Name=="Employer");var oldRole=await db.UserRoles.SingleOrDefaultAsync(x=>x.UserId==owner.Id&&x.RoleId==recruiterRole.Id);if(oldRole is not null)db.UserRoles.Remove(oldRole);if(!await db.UserRoles.AnyAsync(x=>x.UserId==owner.Id&&x.RoleId==employerRole.Id))db.UserRoles.Add(new(){UserId=owner.Id,RoleId=employerRole.Id});
        }else if(r.Feature=="Agency Account"){w.AgencyEnabled=true;}else {w.MasterTalentPoolEnabled=true;w.TeamTalentPoolEnabled=true;}}
        if(r.Status=="Granted"&&input.Status!="Granted")throw new InvalidOperationException("Use Workspace Access controls to revoke a grant; preserve the approval history.");
        r.Status=input.Status;r.AdminResponse=input.AdminResponse.Trim();r.RespondedUtc=DateTime.UtcNow;AtsService.Audit(db,admin.FindFirstValue(ClaimTypes.NameIdentifier)!,"Employer request responded",r.Id.ToString(),r.Status+"; "+r.Feature+"; Agency="+w.AgencyEnabled);await db.SaveChangesAsync();updates?.Publish();
    }
    public async Task<List<PublicCareerJob>> PublicJobs(Guid workspaceId)
    {
        var company=await PublicCompany(workspaceId);if(company is null)return [];
        await using var db=await factory.CreateDbContextAsync();
        return await db.Positions.AsNoTracking().Where(p=>p.WorkspaceId==workspaceId&&p.CareersPublished&&p.Status=="Active")
            .OrderByDescending(p=>p.CreatedUtc).Select(p=>new PublicCareerJob(p.Id,p.Title,p.Location,p.EmploymentType,company.Kind==AccountTypes.Freelance||p.ShowSalaryPublicly?p.SalaryRange:"",p.PublicJobDescription,p.Department,p.WorkArrangement)).ToListAsync();
    }
    public async Task<EmployerWorkspace?> PublicCompany(Guid id)
    {
        await using var db=await factory.CreateDbContextAsync();return await db.EmployerWorkspaces.AsNoTracking().SingleOrDefaultAsync(w=>w.Id==id&&w.CareersPublished&&db.Users.Any(u=>u.Id==w.OwnerId&&u.Status=="Active"&&u.EmailConfirmed)&&(db.Users.Any(u=>u.Id==w.OwnerId&&u.AccountType==AccountTypes.Freelance)||db.UserRoles.Any(ur=>ur.UserId==w.OwnerId&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Employer"))));
    }
    public async Task<PublicCareerApplication> ApplyPublic(Guid workspaceId,Guid positionId,string name,string email,string phone,string city,string fileName,byte[] content)
    {
        var workspace=await PublicCompany(workspaceId)??throw new InvalidOperationException("This Careers Page Is No Longer Available.");
        name=name.Trim();email=email.Trim();phone=phone.Trim();city=city.Trim();
        if(name.Length is <2 or >200)throw new InvalidOperationException("Enter Your Full Name.");
        if(email.Length>320||!new EmailAddressAttribute().IsValid(email))throw new InvalidOperationException("Enter A Valid Email Address.");
        if(phone.Length is <5 or >50)throw new InvalidOperationException("Enter A Valid Phone Number.");
        if(city.Length is <2 or >100)throw new InvalidOperationException("Enter Your City.");
        if(content.Length is 0 or >10*1024*1024)throw new InvalidOperationException("Upload A CV Up To 10 MB.");
        var extension=Path.GetExtension(fileName).ToLowerInvariant();
        if(extension is not(".pdf" or ".docx" or ".txt"))throw new InvalidOperationException("Upload Your CV As A PDF, DOCX Or TXT File.");
        var isPdf=extension==".pdf"&&content.AsSpan().StartsWith("%PDF-"u8);
        var isDocx=extension==".docx"&&content.Length>=4&&content[0]==0x50&&content[1]==0x4b;
        var isText=extension==".txt"&&!content.AsSpan().Contains((byte)0);
        if(!isPdf&&!isDocx&&!isText)throw new InvalidOperationException("The CV File Does Not Match Its File Type.");
        var safeType=extension switch{".pdf"=>"application/pdf",".docx"=>"application/vnd.openxmlformats-officedocument.wordprocessingml.document",_=>"text/plain"};
        await using var db=await factory.CreateDbContextAsync();
        var position=await db.Positions.SingleOrDefaultAsync(p=>p.Id==positionId&&p.WorkspaceId==workspaceId&&p.CareersPublished&&p.Status=="Active")??throw new InvalidOperationException("This Position Is No Longer Accepting Applications.");
        var emailKey=CandidateIdentity.Email(email);var phoneKey=CandidateIdentity.Phone(phone);var workspaceCandidates=await db.Candidates.Where(c=>c.WorkspaceId==workspaceId).ToListAsync();
        var emailMatches=workspaceCandidates.Where(c=>CandidateIdentity.Email(c.Email)==emailKey).ToList();var phoneMatches=phoneKey.Length==0?[]:workspaceCandidates.Where(c=>CandidateIdentity.Phone(c.Phone)==phoneKey).ToList();
        if(emailMatches.Count>1||phoneMatches.Count>1||(emailMatches.Count==1&&phoneMatches.Count==1&&emailMatches[0].Id!=phoneMatches[0].Id))throw new InvalidOperationException("We Could Not Match These Details To One Candidate Profile. Please Contact The Employer Before Applying.");
        var candidate=emailMatches.FirstOrDefault()??phoneMatches.FirstOrDefault();
        if(candidate is not null&&await db.Assessments.AnyAsync(a=>a.PositionId==position.Id&&a.CandidateId==candidate.Id))throw new InvalidOperationException("An Application For This Position Has Already Been Submitted With These Contact Details.");
        var document=new StoredDocument{FileName=Path.GetFileName(fileName),ContentType=safeType,Content=content,ContentHash=Convert.ToHexString(SHA256.HashData(content))};
        db.Documents.Add(document);
        if(candidate is null){
            var ids=await db.Candidates.Select(c=>c.LegacyId).ToListAsync();
            candidate=new Candidate{WorkspaceId=workspaceId,LegacyId=CandidateIdentity.NextDisplayId(ids),Name=name,NormalizedName=Choices.MatchKey(name),Email=emailKey,Phone=phone,NormalizedPhone=phoneKey,City=city,Location=city,DocumentId=document.Id,CvProfileStatus=cvProfiles is null?"Not Requested":"Pending"};
            db.Candidates.Add(candidate);
        }else{
            candidate.Name=name;candidate.NormalizedName=Choices.MatchKey(name);candidate.Email=emailKey;candidate.Phone=phone;candidate.NormalizedPhone=phoneKey;candidate.City=city;candidate.Location=city;candidate.DocumentId=document.Id;candidate.CvProfileStatus=cvProfiles is null?"Not Requested":"Pending";candidate.CvProfileError="";
        }
        await TalentIdentity.Attach(db,candidate);
        var snapshot=new AssessmentSnapshot(candidate,null,[],"Public Careers Page Application","","",position.Client,position.Title,position.Reference);
        var assessment=new Assessment{PositionId=position.Id,CandidateId=candidate.Id,CandidateKey=candidate.LegacyId,DocumentId=document.Id,OwnerId=workspace.OwnerId,CandidateName=candidate.Name,State="Saved",Stage="Applied",Decision="Not Assessed",SnapshotJson=System.Text.Json.JsonSerializer.Serialize(snapshot),SourceFileName=document.FileName};
        db.Assessments.Add(assessment);
        AtsService.Audit(db,"Public Careers Page","Candidate application received",assessment.Id.ToString(),$"Position {position.Reference}; workspace {workspaceId}",candidate.Id,assessment.Id);
        await db.SaveChangesAsync();
        cvProfiles?.Queue(candidate.Id);updates?.Publish();return new(assessment.Id,cvProfiles is not null);
    }
}
