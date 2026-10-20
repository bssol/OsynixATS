using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class WorkspaceUpgrade {
 public static async Task Apply(AtsDbContext db){
  await using var tx=await db.Database.BeginTransactionAsync();
  var users=await db.Users.Where(u=>u.AccountType==AccountTypes.Legacy).ToListAsync();
  foreach(var user in users){
   var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==user.Id select r.Name).ToListAsync();
   if(roles.Contains("Employer"))user.AccountType=AccountTypes.Employer;
   else if(roles.Contains("Recruiter"))user.AccountType=user.EmployerWorkspaceId is null?AccountTypes.Freelance:AccountTypes.Employee;
   else user.AccountType=roles.FirstOrDefault()??AccountTypes.Legacy;
   if(user.EmployerWorkspaceId is Guid team&&!await db.WorkspaceMemberships.AnyAsync(m=>m.UserId==user.Id&&m.WorkspaceId==team))db.WorkspaceMemberships.Add(new(){UserId=user.Id,WorkspaceId=team,Status=user.Status,CanCreateClients=user.CanCreateClients,CanCreatePositions=user.CanCreatePositions,CanExportReports=user.CanExportReports,CanViewTalentPool=user.CanViewTalentPool});
   if(user.AccountType is AccountTypes.Freelance or AccountTypes.Employer){
    var workspace=await EmployerService.EnsureWorkspace(db,user);
    if(user.TalentPoolPaidUntilUtc>DateTime.UtcNow){var entitlement=await db.EmployerWorkspaces.SingleAsync(w=>w.Id==workspace.Id);entitlement.MasterTalentPoolEnabled=true;entitlement.TeamTalentPoolEnabled=true;}
    var positions=await db.Positions.Where(p=>p.WorkspaceId==null&&p.AssignedRecruiter==user.UserName).ToListAsync();
    foreach(var position in positions){position.WorkspaceId=workspace.Id;if(position.ClientId is Guid cid){var client=await db.Clients.Include(c=>c.Contacts).SingleAsync(c=>c.Id==cid);if(client.WorkspaceId!=workspace.Id){var local=await db.Clients.FirstOrDefaultAsync(c=>c.WorkspaceId==workspace.Id&&c.NormalizedName==client.NormalizedName)??db.Clients.Local.FirstOrDefault(c=>c.WorkspaceId==workspace.Id&&c.NormalizedName==client.NormalizedName);if(local is null){local=JsonSerializer.Deserialize<Client>(JsonSerializer.Serialize(client))!;local.Id=Guid.NewGuid();local.WorkspaceId=workspace.Id;local.Revision=Guid.NewGuid();foreach(var contact in local.Contacts){contact.Id=Guid.NewGuid();contact.ClientId=local.Id;contact.Revision=Guid.NewGuid();}db.Clients.Add(local);}position.ClientId=local.Id;}}
    }
   }
  }
  await db.SaveChangesAsync();
  var candidates=await db.Candidates.Include(c=>c.EmploymentHistory).Include(c=>c.EducationHistory).AsSplitQuery().Where(c=>c.PersonId==null).ToListAsync();
  var displayIds=(await db.Candidates.Select(c=>c.LegacyId).ToListAsync()).ToList();
  await TalentIdentity.AttachMany(db,candidates);
  foreach(var candidate in candidates){
   var applications=await db.Assessments.Include(a=>a.Position).Where(a=>a.CandidateId==candidate.Id).ToListAsync();var workspaces=applications.Select(a=>a.Position!.WorkspaceId).Distinct().ToList();
   if(workspaces.Count==1){candidate.WorkspaceId=workspaces[0];}
   else if(workspaces.Count>1){
    var retain=workspaces.Contains(null)?null:workspaces[0];candidate.WorkspaceId=retain;
    foreach(var workspace in workspaces.Where(w=>w!=retain)){
     var display=CandidateIdentity.NextDisplayId(displayIds);displayIds.Add(display);var clone=TalentIdentity.Copy(candidate,workspace,display);db.Candidates.Add(clone);
     foreach(var application in applications.Where(a=>a.Position!.WorkspaceId==workspace)){application.Candidate=null;application.CandidateId=clone.Id;application.CandidateKey=clone.LegacyId;
      foreach(var note in await db.CandidateNotes.Where(n=>n.AssessmentId==application.Id&&n.CandidateId==candidate.Id).ToListAsync())note.CandidateId=clone.Id;
      foreach(var entry in await db.Audit.Where(e=>e.AssessmentId==application.Id&&e.CandidateId==candidate.Id).ToListAsync())entry.CandidateId=clone.Id;
     }
     foreach(var batch in await db.AssessmentBatches.Where(b=>b.SourceCandidateId==candidate.Id&&db.Positions.Any(p=>p.Id==b.PositionId&&p.WorkspaceId==workspace)).ToListAsync())batch.SourceCandidateId=clone.Id;
    }
   }
  }
  await db.SaveChangesAsync();await tx.CommitAsync();
 }
}
