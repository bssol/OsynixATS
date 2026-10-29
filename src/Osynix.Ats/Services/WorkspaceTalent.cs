using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class WorkspaceTalent {
 public static async Task<IQueryable<Candidate>> Query(AtsDbContext db,ClaimsPrincipal principal,ActiveWorkspace active){
 var uid=principal.FindFirstValue(ClaimTypes.NameIdentifier);var account=await db.Users.FindAsync(uid);var username=account?.UserName;
 if(active.Owner&&account?.AccountType==AccountTypes.Freelance){
 var choices=await WorkspaceContext.Choices(db,principal);var teamIds=choices.Where(c=>!c.Owner).Select(c=>c.Id).ToList();
 var allowed=await db.WorkspaceMemberships.Where(m=>m.UserId==uid&&m.Status=="Active"&&m.CanViewTalentPool&&teamIds.Contains(m.WorkspaceId)).Select(m=>m.WorkspaceId).ToListAsync();
 return db.Candidates.Where(c=>c.WorkspaceId==active.Id||db.Assessments.Any(a=>a.CandidateId==c.Id&&a.State=="Saved"&&a.Position!.WorkspaceId!=null&&allowed.Contains(a.Position.WorkspaceId.Value)&&(a.Position.AssignedRecruiter==username||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==a.PositionId&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==username)))));
 }
 if(active.Membership is not null)return db.Candidates.Where(c=>c.WorkspaceId==active.Id&&db.Assessments.Any(a=>a.CandidateId==c.Id&&a.State=="Saved"&&a.Position!.WorkspaceId==active.Id&&(a.Position.AssignedRecruiter==username||db.PositionRecruiterAssignments.Any(pa=>pa.PositionId==a.PositionId&&db.Users.Any(au=>au.Id==pa.UserId&&au.UserName==username)))&&a.Position.Status=="Active"));
 var mode=await ProfileModes.Selected(db,principal,active);
 return db.Candidates.Where(c=>c.WorkspaceId==active.Id&&(mode==null||db.Assessments.Any(a=>a.CandidateId==c.Id&&a.Position!.IsAgencyPosition==mode)));
 }
}
