using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;

public class ProfessionalProfileService(AuthenticationStateProvider auth,IDbContextFactory<AtsDbContext> factory)
{
    async Task<AtsUser> Account(AtsDbContext db){
        var principal=(await auth.GetAuthenticationStateAsync()).User;
        var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if(principal.Identity?.IsAuthenticated!=true||id is null)throw new UnauthorizedAccessException();
        return await db.Users.SingleOrDefaultAsync(u=>u.Id==id&&u.Status=="Active")??throw new UnauthorizedAccessException("Account Is Not Active.");
    }
    public async Task<FreelancerProfile> Get(){await using var db=await factory.CreateDbContextAsync();var user=await Account(db);return await db.FreelancerProfiles.AsNoTracking().SingleOrDefaultAsync(p=>p.UserId==user.Id)??new(){UserId=user.Id};}
    public async Task Save(FreelancerProfile input){
        await using var db=await factory.CreateDbContextAsync();var user=await Account(db);
        if(input.UserId!=user.Id)throw new UnauthorizedAccessException("Only Your Own Profile Can Be Edited.");
        if(input.Published&&(user.AccountType!=AccountTypes.Freelance||!user.EmailConfirmed))throw new UnauthorizedAccessException("Only Verified Freelance Recruiters Can Join The Recruiter Directory.");
        if(input.Headline.Length>160||input.Location.Length>150||input.Skills.Length>1000||input.Technologies.Length>1000||input.Bio.Length>3000||input.Languages.Length>500||input.HiringMarkets.Length>500||input.WeeklyAvailability.Length>200||input.Industries.Length>1000||input.SeniorityLevels.Length>200||input.WorkingStyle.Length>200||input.Experience.Length>20000||input.Education.Length>10000||input.CaseStudies.Length>20000||input.YearsExperience is <0 or >70||input.Availability is not("Available" or "Limited Availability" or "Not Available"))throw new InvalidOperationException("Review The Profile Fields, Experience And Availability.");
        try{TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone);}catch{throw new InvalidOperationException("Enter A Valid Time Zone, Such As Asia/Karachi.");}
        if(input.Published&&(string.IsNullOrWhiteSpace(input.Headline)||string.IsNullOrWhiteSpace(input.Skills)||string.IsNullOrWhiteSpace(input.Bio)))throw new InvalidOperationException("Add A Headline, Skills And Introduction Before Publishing.");
        var saved=await db.FreelancerProfiles.SingleOrDefaultAsync(p=>p.UserId==user.Id);
        if(saved is null){saved=new(){UserId=user.Id};db.FreelancerProfiles.Add(saved);}else if(saved.Revision!=input.Revision)throw new InvalidOperationException("Your Profile Changed. Reload Before Saving.");
        saved.Headline=input.Headline.Trim();saved.Bio=input.Bio.Trim();saved.Location=input.Location.Trim();saved.Skills=input.Skills.Trim();saved.YearsExperience=input.YearsExperience;saved.Availability=input.Availability;saved.Published=input.Published;
        saved.TimeZone=input.TimeZone;saved.Languages=input.Languages.Trim();saved.HiringMarkets=input.HiringMarkets.Trim();saved.WeeklyAvailability=input.WeeklyAvailability.Trim();saved.Industries=input.Industries.Trim();saved.Technologies=input.Technologies.Trim();saved.SeniorityLevels=input.SeniorityLevels.Trim();saved.WorkingStyle=input.WorkingStyle.Trim();saved.Experience=input.Experience.Trim();saved.Education=input.Education.Trim();saved.CaseStudies=input.CaseStudies.Trim();
        AtsService.Audit(db,user.Id,"Professional profile updated",saved.Id.ToString());await db.SaveChangesAsync();
    }
}
