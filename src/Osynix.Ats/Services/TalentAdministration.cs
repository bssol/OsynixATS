using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public record TalentIdentityReview(Guid Id,Guid Revision,string Name,string Email,string Phone,bool NeedsReview);
public class TalentAdministration(Access access,IDbContextFactory<AtsDbContext> factory){
 public async Task<List<TalentIdentityReview>> Review(){await access.Require(true);await using var db=await factory.CreateDbContextAsync();return await (from person in db.TalentPeople.AsNoTracking() join candidate in db.Candidates on person.CanonicalCandidateId equals (Guid?)candidate.Id orderby person.NeedsReview descending,candidate.Name select new TalentIdentityReview(person.Id,person.Revision,candidate.Name,candidate.Email,candidate.Phone,person.NeedsReview)).ToListAsync();}
 public async Task Resolve(Guid id,Guid revision,Guid? mergeInto=null){
  var uid=await access.UserId();await access.Require(true);await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var person=await db.TalentPeople.SingleAsync(p=>p.Id==id);
  if(person.Revision!=revision)throw new InvalidOperationException("Identity Changed. Reload Before Reviewing.");
  if(mergeInto is Guid targetId){if(targetId==id)throw new InvalidOperationException("Choose A Different Target Identity.");var target=await db.TalentPeople.SingleOrDefaultAsync(p=>p.Id==targetId&&p.CanonicalCandidateId!=null)??throw new InvalidOperationException("Target Identity Unavailable.");var profiles=await db.Candidates.Where(c=>c.PersonId==id).ToListAsync();foreach(var profile in profiles)profile.PersonId=target.Id;target.CanonicalCandidateId??=person.CanonicalCandidateId;target.NeedsReview=false;person.CanonicalCandidateId=null;person.NeedsReview=false;AtsService.Audit(db,uid,"Master identity merged",id.ToString(),$"Target={target.Id}; Workspace Profiles And Assessments Preserved.");}
  else{person.NeedsReview=false;AtsService.Audit(db,uid,"Master identity confirmed distinct",id.ToString(),"Admin Confirmed This Is A Separate Person.");}
  await db.SaveChangesAsync();await tx.CommitAsync();
 }
}
