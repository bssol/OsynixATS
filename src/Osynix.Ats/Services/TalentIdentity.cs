using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class TalentIdentity {
 public static IQueryable<Candidate> MasterCandidates(AtsDbContext db,bool includeReview=false)=>db.Candidates.Where(c=>db.TalentPeople.Any(p=>p.CanonicalCandidateId==c.Id));
 public static Candidate? ResolveWorkspace(IEnumerable<Candidate> people,Candidate incoming){
  var name=Choices.MatchKey(incoming.Name);var email=CandidateIdentity.Email(incoming.Email);var phone=CandidateIdentity.Phone(incoming.Phone);
  var matches=people.Where(c=>Choices.MatchKey(c.Name)==name&&name.Length>0&&((email.Length>0&&CandidateIdentity.Email(c.Email)==email)||(phone.Length>0&&CandidateIdentity.Phone(c.Phone)==phone&&(email.Length==0||c.Email.Length==0)))).ToList();
  if(matches.Count>1)throw new InvalidOperationException("Several Workspace Profiles Match This Identity. Ask Admin To Review The Duplicate Records.");return matches.SingleOrDefault();
 }
 public static async Task Attach(AtsDbContext db,Candidate candidate){
  if(candidate.PersonId is not null)return;
  var name=Choices.MatchKey(candidate.Name);var email=CandidateIdentity.Email(candidate.Email);var phone=CandidateIdentity.Phone(candidate.Phone);
  var exact=await db.TalentPeople.Where(p=>(email.Length>0&&p.EmailKey==email)||(phone.Length>0&&p.PhoneKey==phone&&name.Length>0&&p.NameKey==name)).OrderBy(p=>p.CreatedUtc).ToListAsync();
  TalentPerson person;
  if(exact.Count>0){person=exact[0];if(person.EmailKey.Length==0)person.EmailKey=email;if(person.PhoneKey.Length==0)person.PhoneKey=phone;person.NeedsReview=exact.Count>1;foreach(var duplicate in exact.Skip(1))duplicate.NeedsReview=true;}
  else{person=new(){NameKey=name,EmailKey=email,PhoneKey=phone,NeedsReview=false};db.TalentPeople.Add(person);}
  candidate.PersonId=person.Id;person.CanonicalCandidateId??=candidate.Id;
 }
 public static async Task AttachMany(AtsDbContext db,IEnumerable<Candidate> candidates){
  var identities=await db.TalentPeople.OrderBy(p=>p.CreatedUtc).ToListAsync();
  var emails=identities.Where(p=>p.EmailKey.Length>0).GroupBy(p=>p.EmailKey).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
  var phoneNames=identities.Where(p=>p.PhoneKey.Length>0&&p.NameKey.Length>0).GroupBy(p=>p.PhoneKey+"|"+p.NameKey).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
  foreach(var candidate in candidates.Where(c=>c.PersonId is null)){
   var name=Choices.MatchKey(candidate.Name);var email=CandidateIdentity.Email(candidate.Email);var phone=CandidateIdentity.Phone(candidate.Phone);
   List<TalentPerson>? matches=null;
   if(email.Length>0)emails.TryGetValue(email,out matches);
   if(matches is null&&phone.Length>0&&name.Length>0)phoneNames.TryGetValue(phone+"|"+name,out matches);
   TalentPerson person;
   if(matches is {Count:>0}){
    person=matches[0];
    if(matches.Count>1)foreach(var duplicate in matches)duplicate.NeedsReview=true;
    if(person.EmailKey.Length==0&&email.Length>0)person.EmailKey=email;
    if(person.PhoneKey.Length==0&&phone.Length>0)person.PhoneKey=phone;
    person.CanonicalCandidateId??=candidate.Id;
   }else{
    person=new(){NameKey=name,EmailKey=email,PhoneKey=phone,CanonicalCandidateId=candidate.Id};db.TalentPeople.Add(person);identities.Add(person);
    if(email.Length>0){if(!emails.TryGetValue(email,out var emailGroup))emails[email]=emailGroup=[];emailGroup.Add(person);}
    if(phone.Length>0&&name.Length>0){var key=phone+"|"+name;if(!phoneNames.TryGetValue(key,out var phoneGroup))phoneNames[key]=phoneGroup=[];phoneGroup.Add(person);}
   }
   candidate.PersonId=person.Id;
  }
 }
 public static async Task Consolidate(AtsDbContext db){
  var pending=await db.TalentPeople.Where(p=>p.NeedsReview).ToListAsync();
  foreach(var seed in pending){
   if(!seed.NeedsReview)continue;
   var sameEmail=seed.EmailKey.Length>0?await db.TalentPeople.Where(p=>p.EmailKey==seed.EmailKey).ToListAsync():[];
   var samePhone=seed.PhoneKey.Length>0&&seed.NameKey.Length>0?await db.TalentPeople.Where(p=>p.PhoneKey==seed.PhoneKey&&p.NameKey==seed.NameKey).ToListAsync():[];
   var group=sameEmail.Concat(samePhone).Append(seed).DistinctBy(p=>p.Id).ToList();
   if(group.Count<2){seed.NeedsReview=false;continue;}
   var ids=group.Select(p=>p.Id).ToList();
   var candidateRows=await db.Candidates.Where(c=>c.PersonId!=null&&ids.Contains(c.PersonId.Value)).OrderBy(c=>c.CreatedUtc).ToListAsync();
   var canonical=group.Where(p=>p.CanonicalCandidateId!=null).Join(candidateRows,p=>p.CanonicalCandidateId,c=>c.Id,(p,c)=>new{Person=p,Candidate=c}).OrderBy(x=>x.Candidate.CreatedUtc).FirstOrDefault();
   if(canonical is null){foreach(var p in group)p.NeedsReview=false;continue;}
   var root=canonical.Person;
   MergeProfile(canonical.Candidate,candidateRows.Where(c=>c.Id!=canonical.Candidate.Id).OrderByDescending(c=>c.UpdatedUtc));
   root.NameKey=root.NameKey.Length>0?root.NameKey:group.FirstOrDefault(p=>p.NameKey.Length>0)?.NameKey??"";
   root.EmailKey=root.EmailKey.Length>0?root.EmailKey:group.FirstOrDefault(p=>p.EmailKey.Length>0)?.EmailKey??"";
   root.PhoneKey=root.PhoneKey.Length>0?root.PhoneKey:group.FirstOrDefault(p=>p.PhoneKey.Length>0)?.PhoneKey??"";
   root.NeedsReview=false;
   foreach(var row in candidateRows)row.PersonId=root.Id;
   foreach(var p in group.Where(p=>p.Id!=root.Id)){p.CanonicalCandidateId=null;p.NeedsReview=false;}
  }
  if(pending.Count>0)await db.SaveChangesAsync();
 }
 static void MergeProfile(Candidate target,IEnumerable<Candidate> sources){
  var rows=sources.ToList();
  string First(string current,Func<Candidate,string> read)=>current.Length>0?current:rows.Select(read).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"";
  target.Email=First(target.Email,c=>c.Email);target.Phone=First(target.Phone,c=>c.Phone);target.LinkedInUrl=First(target.LinkedInUrl,c=>c.LinkedInUrl);
  target.City=First(target.City,c=>CandidateCity.Display(c));target.Location=First(target.Location,c=>CandidateCity.Display(c));
  target.CurrentRole=First(target.CurrentRole,c=>c.CurrentRole);target.CurrentCompany=First(target.CurrentCompany,c=>c.CurrentCompany);
  target.Summary=First(target.Summary,c=>c.Summary);target.CareerSummary=First(target.CareerSummary,c=>c.CareerSummary);
  target.Skills=First(target.Skills,c=>c.Skills);target.Function=First(target.Function,c=>c.Function);target.Specialization=First(target.Specialization,c=>c.Specialization);
  target.Industry=First(target.Industry,c=>c.Industry);target.Seniority=First(target.Seniority,c=>c.Seniority);target.Systems=First(target.Systems,c=>c.Systems);target.Markets=First(target.Markets,c=>c.Markets);
  target.Employment=First(target.Employment,c=>c.Employment);target.Education=First(target.Education,c=>c.Education);
  target.TotalExperience??=rows.Select(c=>c.TotalExperience).FirstOrDefault(x=>x is not null);
 }
 public static Candidate Copy(Candidate source,Guid? workspaceId,string displayId){
  var clone=JsonSerializer.Deserialize<Candidate>(JsonSerializer.Serialize(source))!;clone.Id=Guid.NewGuid();clone.Revision=Guid.NewGuid();clone.WorkspaceId=workspaceId;clone.LegacyId=displayId;clone.Document=null;clone.Notes="";
  foreach(var job in clone.EmploymentHistory){job.Id=Guid.NewGuid();job.CandidateId=clone.Id;job.Revision=Guid.NewGuid();}foreach(var education in clone.EducationHistory){education.Id=Guid.NewGuid();education.CandidateId=clone.Id;education.Revision=Guid.NewGuid();}return clone;
 }
}
