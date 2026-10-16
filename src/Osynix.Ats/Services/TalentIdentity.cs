using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class TalentIdentity {
 public static IQueryable<Candidate> MasterCandidates(AtsDbContext db,bool includeReview=false)=>db.Candidates.Where(c=>db.TalentPeople.Any(p=>p.CanonicalCandidateId==c.Id&&(includeReview||!p.NeedsReview)));
 public static Candidate? ResolveWorkspace(IEnumerable<Candidate> people,Candidate incoming){
  var name=Choices.MatchKey(incoming.Name);var email=CandidateIdentity.Email(incoming.Email);var phone=CandidateIdentity.Phone(incoming.Phone);
  var matches=people.Where(c=>Choices.MatchKey(c.Name)==name&&name.Length>0&&((email.Length>0&&CandidateIdentity.Email(c.Email)==email)||(phone.Length>0&&CandidateIdentity.Phone(c.Phone)==phone&&(email.Length==0||c.Email.Length==0)))).ToList();
  if(matches.Count>1)throw new InvalidOperationException("Several Workspace Profiles Match This Identity. Ask Admin To Review The Duplicate Records.");return matches.SingleOrDefault();
 }
 public static async Task Attach(AtsDbContext db,Candidate candidate){
  if(candidate.PersonId is not null)return;
  var name=Choices.MatchKey(candidate.Name);var email=CandidateIdentity.Email(candidate.Email);var phone=CandidateIdentity.Phone(candidate.Phone);
  var found=await db.TalentPeople.Where(p=>(email!=""&&p.EmailKey==email)||(phone!=""&&p.PhoneKey==phone)||(email==""&&phone==""&&name!=""&&p.NameKey==name)).ToListAsync();
  found=found.Concat(db.TalentPeople.Local.Where(p=>(email!=""&&p.EmailKey==email)||(phone!=""&&p.PhoneKey==phone)||(email==""&&phone==""&&name!=""&&p.NameKey==name))).DistinctBy(p=>p.Id).ToList();
  var exact=found.Where(p=>name.Length>0&&p.NameKey==name&&((email.Length>0&&p.EmailKey==email)||(phone.Length>0&&p.PhoneKey==phone&&(email.Length==0||p.EmailKey.Length==0)))).ToList();
  TalentPerson person;
  if(exact.Count==1){person=exact[0];if(person.EmailKey.Length==0)person.EmailKey=email;if(person.PhoneKey.Length==0)person.PhoneKey=phone;}
  else{person=new(){NameKey=name,EmailKey=email,PhoneKey=phone,NeedsReview=found.Count>0};foreach(var suspect in found)suspect.NeedsReview=true;db.TalentPeople.Add(person);}
  candidate.PersonId=person.Id;person.CanonicalCandidateId=candidate.Id;
 }
 public static Candidate Copy(Candidate source,Guid? workspaceId,string displayId){
  var clone=JsonSerializer.Deserialize<Candidate>(JsonSerializer.Serialize(source))!;clone.Id=Guid.NewGuid();clone.Revision=Guid.NewGuid();clone.WorkspaceId=workspaceId;clone.LegacyId=displayId;clone.Document=null;clone.Notes="";
  foreach(var job in clone.EmploymentHistory){job.Id=Guid.NewGuid();job.CandidateId=clone.Id;job.Revision=Guid.NewGuid();}foreach(var education in clone.EducationHistory){education.Id=Guid.NewGuid();education.CandidateId=clone.Id;education.Revision=Guid.NewGuid();}return clone;
 }
}
