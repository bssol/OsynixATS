using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record RecruiterChoice(string Username, string Name);
public record CandidateActivity(AuditEntry Event, string CandidateName, string Role);
public class WorkspaceService(IDbContextFactory<AtsDbContext> factory,Access access)
{
    public async Task<List<RecruiterChoice>> Recruiters(Guid? workspaceFilter=null)
    {
        var user = await access.Require();
        await using var db = await factory.CreateDbContextAsync();
        var uid = await access.UserId();
        var query = from account in db.Users.AsNoTracking()
                    join membership in db.UserRoles on account.Id equals membership.UserId
                    join role in db.Roles on membership.RoleId equals role.Id
                    where account.Status == "Active" && role.Name == "Recruiter"
                    select account;
        if(user.IsInRole("Employer")&&!user.IsInRole("Admin")){var workspaceId=await access.WorkspaceId();var account=await db.Users.FindAsync(uid);var team=await query.Where(u=>u.EmployerWorkspaceId==workspaceId).OrderBy(u=>u.FullName).Select(u=>new RecruiterChoice(u.UserName!,u.FullName)).ToListAsync();team.Insert(0,new(user.Identity!.Name!,account!.FullName+" (You)"));return team;}
        if (!user.IsInRole("Admin"))
        {
            var account = await db.Users.FindAsync(uid);
            return [new(user.Identity!.Name!, account?.FullName ?? user.Identity.Name!)];
        }
        if(workspaceFilter is Guid teamId){var members=await query.Where(u=>u.EmployerWorkspaceId==teamId).OrderBy(u=>u.UserName).Select(u=>new RecruiterChoice(u.UserName!,u.FullName)).ToListAsync();var owner=await (from w in db.EmployerWorkspaces join u in db.Users on w.OwnerId equals u.Id where w.Id==teamId&&u.Status=="Active" select u).SingleOrDefaultAsync();if(owner is not null)members.Insert(0,new(owner.UserName!,owner.FullName+" (Employer)"));return members;}
        return await query.Where(u=>u.EmployerWorkspaceId==null).OrderBy(x=>x.UserName).Select(x=>new RecruiterChoice(x.UserName!, x.FullName)).ToListAsync();
    }
    public async Task<List<CandidateActivity>> RecentActivity(int page = 0, int size = 5)
    {
        var user = await access.Require();
        var uid = await access.UserId();var workspaceId=await access.WorkspaceId();
        await using var db = await factory.CreateDbContextAsync();
        return await (from entry in db.Audit.AsNoTracking()
                      join candidate in db.Candidates.AsNoTracking() on entry.CandidateId equals (Guid?)candidate.Id
                      where user.IsInRole("Admin") || db.Assessments.Any(a=>a.Id==entry.AssessmentId&&(user.IsInRole("Employer")||a.Position!.AssignedRecruiter==user.Identity!.Name)&&a.Position!.WorkspaceId==workspaceId) || (entry.AssessmentId==null&&(entry.UserId==uid||(user.IsInRole("Employer")&&db.Users.Any(u=>u.Id==entry.UserId&&u.EmployerWorkspaceId==workspaceId)))&&db.Assessments.Any(a=>a.CandidateId==candidate.Id&&(user.IsInRole("Employer")||a.Position!.AssignedRecruiter==user.Identity!.Name)&&a.Position!.WorkspaceId==workspaceId))
                      orderby entry.CreatedUtc descending, entry.Id descending
                      select new CandidateActivity(entry, candidate.Name, candidate.CurrentRole))
            .Skip(Math.Max(0, page) * Math.Clamp(size, 1, 101)).Take(Math.Clamp(size, 1, 101)).ToListAsync();
    }
    public async Task<List<Client>> Clients()
    {
        var user=await access.Require();var scoped=Access.Scoped(user);var workspaceId=await access.WorkspaceId();var name=user.Identity!.Name;var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        return await db.Clients.Include(x=>x.Contacts).AsNoTracking().Where(c=>!scoped||c.WorkspaceId==workspaceId&&(workspaceId!=null||db.Positions.Any(p=>p.ClientId==c.Id&&((user.IsInRole("Employer")||p.AssignedRecruiter==name)&&p.WorkspaceId==workspaceId))||db.Audit.Any(a=>a.EntityId==c.Id.ToString()&&a.UserId==uid&&a.Action=="Client saved"))).OrderBy(x=>x.Name).ToListAsync();
    }
    public async Task SaveClient(Client input)
    {
        var uid=await access.UserId();await access.RequirePermission("Clients.Create");var workspaceId=await access.WorkspaceId();await using var db=await factory.CreateDbContextAsync();
        if(string.IsNullOrWhiteSpace(input.Name)||input.Status is not ("Active" or "Inactive" or "Archived")) throw new InvalidOperationException("Client name and a valid status are required.");
        var key=Choices.MatchKey(input.Name);if(key.Length==0) throw new InvalidOperationException("Enter a client name.");
        var c=await db.Clients.Include(x=>x.Contacts).SingleOrDefaultAsync(x=>x.Id==input.Id);
        var targetWorkspace=c?.WorkspaceId??workspaceId;
        if(await db.Clients.AnyAsync(x=>x.Id!=input.Id&&x.WorkspaceId==targetWorkspace&&x.NormalizedName==key)) throw new InvalidOperationException("This client already exists. Use the existing client record.");
        if(c is not null&&Access.Scoped(await access.Require())&&!(await Clients()).Any(x=>x.Id==c.Id))throw new UnauthorizedAccessException("This client is not in your workspace.");
        if(c is null) {c=input;c.WorkspaceId=workspaceId;db.Clients.Add(c);}
        else {
            if(c.Revision!=input.Revision) throw new InvalidOperationException("This client changed. Reload before saving.");
            c.Name=input.Name.Trim();c.Industry=input.Industry;c.Location=input.Location;c.Website=input.Website;c.Notes=input.Notes;c.Status=input.Status;
            db.ClientContacts.RemoveRange(c.Contacts);
            c.Contacts=input.Contacts.Where(x=>x.Name.Length>0||x.Email.Length>0).Select(x=>new ClientContact{Name=x.Name,Title=x.Title,Email=x.Email,Phone=x.Phone}).ToList();
            db.ClientContacts.AddRange(c.Contacts);
            foreach(var position in await db.Positions.Where(x=>x.ClientId==c.Id).ToListAsync()) position.Client=c.Name;
        }
        c.Name=c.Name.Trim();c.NormalizedName=key;
        AtsService.Audit(db,uid,"Client saved",c.Id.ToString(),c.Name);await db.SaveChangesAsync();
    }
    public async Task RemoveUnusedClient(Guid id,Guid revision)
    {
        var uid=await access.UserId();await access.RequirePermission("Clients.Delete");await access.RequireClient(id);
        await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        var client=await db.Clients.SingleOrDefaultAsync(c=>c.Id==id)??throw new InvalidOperationException("Client Not Found.");
        if(client.Revision!=revision)throw new DbUpdateConcurrencyException("Client Changed. Reload Before Removing It.");
        if(await db.Positions.AnyAsync(p=>p.ClientId==id)||await db.HiringReviewGrants.AnyAsync(g=>g.ClientId==id))throw new InvalidOperationException("This Client Has Recruitment History. Archive It Instead.");
        db.Clients.Remove(client);AtsService.Audit(db,uid,"Unused client removed",id.ToString(),client.Name);await db.SaveChangesAsync();await tx.CommitAsync();
    }
    public async Task<List<CandidateNote>> Notes(Guid candidateId)
    {
        await access.Require();await using var db=await factory.CreateDbContextAsync();
        await access.RequireCandidate(candidateId);var user=await access.Require();var scoped=Access.Scoped(user);var workspaceId=await access.WorkspaceId();var name=user.Identity!.Name;var uid=await access.UserId();return await db.CandidateNotes.AsNoTracking().Where(x=>x.CandidateId==candidateId&&(!scoped||(x.AssessmentId==null&&(x.AuthorId==uid||(user.IsInRole("Employer")&&db.Users.Any(u=>u.Id==x.AuthorId&&u.EmployerWorkspaceId==workspaceId))))||db.Assessments.Any(a=>a.Id==x.AssessmentId&&(user.IsInRole("Employer")||a.Position!.AssignedRecruiter==name)&&a.Position!.WorkspaceId==workspaceId))).OrderByDescending(x=>x.CreatedUtc).ToListAsync();
    }
    public async Task AddNote(Guid candidateId,Guid? applicationId,string text)
    {
        var user=await access.Require();var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        if(string.IsNullOrWhiteSpace(text)||text.Length>10000) throw new InvalidOperationException("Write a note of up to 10,000 characters.");
        await access.RequireCandidate(candidateId);if(!await db.Candidates.AnyAsync(x=>x.Id==candidateId)) throw new InvalidOperationException("Candidate not found.");
        if(applicationId is Guid linkedApplication)await access.RequireApplication(linkedApplication);
        if(applicationId is not null&&!await db.Assessments.AnyAsync(x=>x.Id==applicationId&&x.CandidateId==candidateId&&x.State=="Saved")) throw new InvalidOperationException("Choose this candidate's application for the note.");
        var note=new CandidateNote{CandidateId=candidateId,AssessmentId=applicationId,Text=text.Trim(),AuthorId=uid,AuthorName=user.Identity!.Name!};db.CandidateNotes.Add(note);
        AtsService.Audit(db,uid,"Note added",note.Id.ToString(),note.Text,candidateId,applicationId);await db.SaveChangesAsync();
    }
    public async Task<List<AuditEntry>> Activity(Guid candidateId)
    {
        await access.Require();await using var db=await factory.CreateDbContextAsync();
        await access.RequireCandidate(candidateId);var user=await access.Require();var scoped=Access.Scoped(user);var workspaceId=await access.WorkspaceId();var name=user.Identity!.Name;var uid=await access.UserId();var events=await db.Audit.AsNoTracking().Where(x=>x.CandidateId==candidateId&&(!scoped||(x.AssessmentId==null&&(x.UserId==uid||(user.IsInRole("Employer")&&db.Users.Any(u=>u.Id==x.UserId&&u.EmployerWorkspaceId==workspaceId))))||db.Assessments.Any(a=>a.Id==x.AssessmentId&&(user.IsInRole("Employer")||a.Position!.AssignedRecruiter==name)&&a.Position!.WorkspaceId==workspaceId))).OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync();
        var users=await db.Users.AsNoTracking().ToDictionaryAsync(x=>x.Id);
        foreach(var e in events) if(e.ActorName.Length==0&&users.TryGetValue(e.UserId,out var actor)) e.ActorName=actor.FullName.Length>0?actor.FullName:actor.UserName??e.UserId;
        return events;
    }
    public async Task EditNote(Guid id,string text)
    {
        var user=await access.Require();var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        if(string.IsNullOrWhiteSpace(text)||text.Length>10000) throw new InvalidOperationException("Write a note of up to 10,000 characters.");
        var note=await db.CandidateNotes.SingleAsync(x=>x.Id==id);await access.RequireCandidate(note.CandidateId);
        if(note.AuthorId!=uid&&!user.IsInRole("Admin")) throw new UnauthorizedAccessException("Only the author or an administrator can edit this note.");
        var old=note.Text;note.Text=text.Trim();
        AtsService.Audit(db,uid,"Note edited",note.Id.ToString(),note.Text,note.CandidateId,note.AssessmentId,JsonSerializer.Serialize(new{Text=old}),JsonSerializer.Serialize(new{note.Text}));await db.SaveChangesAsync();
    }
    public async Task<List<AssessmentBatch>> Batches()
    {
        var uid=await access.UserId();var user=await access.Require();var scoped=Access.Scoped(user);var workspaceId=await access.WorkspaceId();var name=user.Identity!.Name;await using var db=await factory.CreateDbContextAsync();
        return await db.AssessmentBatches.Include(x=>x.Items).AsNoTracking().Where(x=>x.OwnerId==uid&&(!scoped||db.Positions.Any(p=>p.Id==x.PositionId&&(user.IsInRole("Employer")||p.AssignedRecruiter==name)&&p.WorkspaceId==workspaceId))).OrderByDescending(x=>x.CreatedUtc).Take(25).ToListAsync();
    }
    public async Task<Guid> CreateBatch(Guid positionId,string[] fileNames,Guid? sourceCandidateId=null,bool refreshProfile=false)
    {
        var uid=await access.UserId();if(refreshProfile&&sourceCandidateId is null)throw new InvalidOperationException("Select the existing candidate for an explicit profile refresh.");if(fileNames.Length is <1 or >5) throw new InvalidOperationException("Select between one and five CVs.");
        await using var db=await factory.CreateDbContextAsync();
        await access.RequirePosition(positionId);if(sourceCandidateId is Guid cid)await access.RequireCandidate(cid);if(!await db.Positions.AnyAsync(x=>x.Id==positionId&&x.Status=="Active"&&x.CriteriaLocked)) throw new InvalidOperationException("Choose an active position with reviewed criteria.");
        if(sourceCandidateId is not null&&(fileNames.Length!=1||!await db.Candidates.AnyAsync(x=>x.Id==sourceCandidateId))) throw new InvalidOperationException("Choose one CV and an existing candidate for a profile refresh.");
        var batch=new AssessmentBatch{PositionId=positionId,OwnerId=uid,SourceCandidateId=sourceCandidateId,RefreshProfile=refreshProfile,Mode=fileNames.Length==1?"Single":"Batch",Items=fileNames.Select((name,index)=>new BatchItem{Index=index,FileName=Path.GetFileName(name)}).ToList()};
        db.AssessmentBatches.Add(batch);await db.SaveChangesAsync();return batch.Id;
    }
    public async Task SetBatchItem(Guid batchId,int index,Guid? assessmentId,string error="")
    {
        var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        var batch=await db.AssessmentBatches.Include(x=>x.Items).SingleAsync(x=>x.Id==batchId&&x.OwnerId==uid);
        await access.RequirePosition(batch.PositionId);
        var item=batch.Items.Single(x=>x.Index==index);
        if(item.State=="Saved") return;
        if(assessmentId is not null&&!await db.Assessments.AnyAsync(x=>x.Id==assessmentId&&x.BatchId==batchId&&x.BatchIndex==index&&x.OwnerId==uid)) throw new InvalidOperationException("The assessment does not belong to this batch item.");
        item.AssessmentId=assessmentId;item.State=assessmentId is null?"Failed":"Draft";item.Error=error;
        await db.SaveChangesAsync();
    }
    public async Task CompleteBatch(Guid batchId)
    {
        var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        var batch=await db.AssessmentBatches.Include(x=>x.Items).SingleAsync(x=>x.Id==batchId&&x.OwnerId==uid);
        await access.RequirePosition(batch.PositionId);
        if(batch.Items.Any(x=>x.State!="Saved")) throw new InvalidOperationException("Review failed/unsaved items before completing this batch.");
        batch.State="Completed";await db.SaveChangesAsync();
    }
}
