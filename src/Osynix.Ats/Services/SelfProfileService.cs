using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
namespace Osynix.Ats.Services;
public record SelfProfile(string Name,string Image,string Revision);
public class SelfProfileService(AuthenticationStateProvider auth,IDbContextFactory<AtsDbContext> factory,RecruitmentUpdates updates){
 public static string ImageUrl(AtsUser account)=>account.ProfileImage is {Length:>0}&&account.ProfileImageType is "image/png" or "image/jpeg"?"data:"+account.ProfileImageType+";base64,"+Convert.ToBase64String(account.ProfileImage):"/images/profile-avatar.svg";
 async Task<AtsUser> Account(AtsDbContext db){var principal=(await auth.GetAuthenticationStateAsync()).User;var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);if(principal.Identity?.IsAuthenticated!=true||id is null)throw new UnauthorizedAccessException();var account=await db.Users.SingleOrDefaultAsync(u=>u.Id==id&&u.Status=="Active")??throw new UnauthorizedAccessException("Account Is Not Active.");return account;}
 public async Task<SelfProfile> Get(){await using var db=await factory.CreateDbContextAsync();var user=await Account(db);return new(user.FullName,ImageUrl(user),user.ConcurrencyStamp??"");}
 public async Task UpdateName(string name,string revision){name=name.Trim();if(name.Length is <1 or >100||name.Any(char.IsControl))throw new InvalidOperationException("Enter A Name Of 1–100 Characters.");await using var db=await factory.CreateDbContextAsync();var user=await Account(db);Check(user,revision);user.FullName=name;user.ConcurrencyStamp=Guid.NewGuid().ToString();AtsService.Audit(db,user.Id,"Profile name updated",user.Id);await db.SaveChangesAsync();updates.Publish();}
 public async Task UpdateImage(byte[] bytes,string revision){if(bytes.Length>1024*1024)throw new InvalidOperationException("Choose A Profile Photo Up To 1 MB.");var png=bytes.Length>24&&bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10});var jpeg=bytes.Length>24&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255;if(!png&&!jpeg)throw new InvalidOperationException("Choose A PNG Or JPEG Profile Photo.");await using var db=await factory.CreateDbContextAsync();var user=await Account(db);Check(user,revision);user.ProfileImage=bytes.ToArray();user.ProfileImageType=png?"image/png":"image/jpeg";user.ConcurrencyStamp=Guid.NewGuid().ToString();AtsService.Audit(db,user.Id,"Profile image updated",user.Id);await db.SaveChangesAsync();updates.Publish();}
 static void Check(AtsUser account,string revision){if((account.ConcurrencyStamp??"")!=revision)throw new InvalidOperationException("Your Profile Changed. Reload Before Saving.");}
}
