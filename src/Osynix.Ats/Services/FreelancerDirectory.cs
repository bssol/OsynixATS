using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;

public record RecruiterListing(string Id,string Name,string Headline,string Skills,string Bio,string Location,int YearsExperience,string Availability);
public record RecruiterDetail(string Id,string Name,string Image,FreelancerProfile Profile);

public class FreelancerDirectory(Access access,EmployerTeamService teams,IDbContextFactory<AtsDbContext> factory,RecruitmentUpdates? updates=null)
{
    public async Task<RecruiterDetail> Details(string id)
    {
        await teams.Team();await using var db=await factory.CreateDbContextAsync();
        await SubscriptionRules.RequireMarketplace(db,id);
        var user=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==id);
        var profile=await db.FreelancerProfiles.AsNoTracking().SingleOrDefaultAsync(p=>p.UserId==id)??new(){UserId=id};
        profile.Published=true;
        return new(user.Id,user.FullName,SelfProfileService.ImageUrl(user),profile);
    }

    public async Task<FreelancerProfile?> MyProfile()
    {
        var id=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        var user=await db.Users.FindAsync(id);if(user?.AccountType!=AccountTypes.Freelance)return null;
        var profile=await db.FreelancerProfiles.AsNoTracking().SingleOrDefaultAsync(p=>p.UserId==id)??new(){UserId=id};
        profile.Published=await SubscriptionRules.MarketplaceAllowed(db,id);
        return profile;
    }

    public async Task Save(FreelancerProfile input)
    {
        var id=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        if(!await db.Users.AnyAsync(u=>u.Id==id&&u.AccountType==AccountTypes.Freelance&&u.Status=="Active"&&u.EmailConfirmed))throw new UnauthorizedAccessException();
        if(input.Headline.Length>160||input.Skills.Length>1000||input.Bio.Length>3000||input.Location.Length>150||input.YearsExperience is <0 or >70||input.Availability is not("Available" or "Limited Availability" or "Not Available"))throw new InvalidOperationException("Review Your Profile Lengths, Experience And Availability.");
        var saved=await db.FreelancerProfiles.SingleOrDefaultAsync(p=>p.UserId==id);
        if(saved is null){saved=new(){UserId=id};db.Add(saved);}else if(saved.Revision!=input.Revision)throw new InvalidOperationException("Your Profile Changed. Reload Before Saving.");
        saved.Published=await SubscriptionRules.MarketplaceAllowed(db,id);
        saved.Headline=input.Headline.Trim();saved.Skills=input.Skills.Trim();saved.Bio=input.Bio.Trim();saved.Location=input.Location.Trim();saved.YearsExperience=input.YearsExperience;saved.Availability=input.Availability;
        AtsService.Audit(db,id,"Freelancer directory profile updated",saved.Id.ToString());await db.SaveChangesAsync();updates?.Publish();
    }

    public async Task<List<RecruiterListing>> Search()
    {
        await teams.Team();await using var db=await factory.CreateDbContextAsync();
        await SubscriptionLifecycle.ApplyDue(db);
        var workspaces=await db.EmployerWorkspaces.AsNoTracking().Where(w=>w.Kind==AccountTypes.Freelance).ToListAsync();
        var visibleIds=workspaces.Where(w=>SubscriptionRules.Terms(w)?.Marketplace==true).Select(w=>w.OwnerId).ToList();
        return await(from user in db.Users.AsNoTracking()
                     join profile in db.FreelancerProfiles.AsNoTracking() on user.Id equals profile.UserId into profiles
                     from profile in profiles.DefaultIfEmpty()
                     where visibleIds.Contains(user.Id)&&user.AccountType==AccountTypes.Freelance&&user.Status=="Active"&&user.EmailConfirmed
                     orderby user.FullName
                     select new RecruiterListing(user.Id,user.FullName,profile==null?"Freelance Recruiter":profile.Headline,profile==null?"":profile.Skills,profile==null?"":profile.Bio,profile==null?"":profile.Location,profile==null?0:profile.YearsExperience,profile==null?"Available":profile.Availability)).ToListAsync();
    }

    public async Task<ForwardResult> Invite(string id)
    {
        await teams.Team();await using var db=await factory.CreateDbContextAsync();
        await SubscriptionRules.RequireMarketplace(db,id);
        var user=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==id);
        return await teams.Invite(user.FullName,user.Email!);
    }
}
