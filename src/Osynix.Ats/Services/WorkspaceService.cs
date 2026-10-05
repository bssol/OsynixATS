using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public class WorkspaceService(IDbContextFactory<AtsDbContext> factory,Access access)
{
    public async Task<List<Client>> Clients()
    {
        await access.Require();await using var db=await factory.CreateDbContextAsync();
        return await db.Clients.Include(x=>x.Contacts).AsNoTracking().OrderBy(x=>x.Name).ToListAsync();
    }
    public async Task SaveClient(Client input)
    {
        var uid=await access.UserId();await access.RequirePermission("Positions.Create");await using var db=await factory.CreateDbContextAsync();
        if(string.IsNullOrWhiteSpace(input.Name)||input.Status is not ("Active" or "Inactive")) throw new InvalidOperationException("Client name and a valid status are required.");
        var key=Choices.MatchKey(input.Name);if(key.Length==0) throw new InvalidOperationException("Enter a client name.");
        if(await db.Clients.AnyAsync(x=>x.Id!=input.Id&&x.NormalizedName==key)) throw new InvalidOperationException("This client already exists. Use the existing client record.");
        var c=await db.Clients.Include(x=>x.Contacts).SingleOrDefaultAsync(x=>x.Id==input.Id);
        if(c is null) {c=input;db.Clients.Add(c);}
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
    public async Task<List<CandidateNote>> Notes(Guid candidateId)
    {
        await access.Require();await using var db=await factory.CreateDbContextAsync();
        return await db.CandidateNotes.AsNoTracking().Where(x=>x.CandidateId==candidateId).OrderByDescending(x=>x.CreatedUtc).ToListAsync();
    }
    public async Task AddNote(Guid candidateId,Guid? applicationId,string text)
    {
        var user=await access.Require();var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        if(string.IsNullOrWhiteSpace(text)||text.Length>10000) throw new InvalidOperationException("Write a note of up to 10,000 characters.");
        if(!await db.Candidates.AnyAsync(x=>x.Id==candidateId)) throw new InvalidOperationException("Candidate not found.");
        if(applicationId is not null&&!await db.Assessments.AnyAsync(x=>x.Id==applicationId&&x.CandidateId==candidateId&&x.State=="Saved")) throw new InvalidOperationException("Choose this candidate's application for the note.");
        var note=new CandidateNote{CandidateId=candidateId,AssessmentId=applicationId,Text=text.Trim(),AuthorId=uid,AuthorName=user.Identity!.Name!};db.CandidateNotes.Add(note);
        AtsService.Audit(db,uid,"Note added",note.Id.ToString(),note.Text,candidateId,applicationId);await db.SaveChangesAsync();
    }
    public async Task<List<AuditEntry>> Activity(Guid candidateId)
    {
        await access.Require();await using var db=await factory.CreateDbContextAsync();
        var events=await db.Audit.AsNoTracking().Where(x=>x.CandidateId==candidateId).OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync();
        var users=await db.Users.AsNoTracking().ToDictionaryAsync(x=>x.Id);
        foreach(var e in events) if(e.ActorName.Length==0&&users.TryGetValue(e.UserId,out var user)) e.ActorName=user.FullName.Length>0?user.FullName:user.UserName??e.UserId;
        return events;
    }
    public async Task EditNote(Guid id,string text)
    {
        var user=await access.Require();var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        if(string.IsNullOrWhiteSpace(text)||text.Length>10000) throw new InvalidOperationException("Write a note of up to 10,000 characters.");
        var note=await db.CandidateNotes.SingleAsync(x=>x.Id==id);
        if(note.AuthorId!=uid&&!user.IsInRole("Admin")) throw new UnauthorizedAccessException("Only the author or an administrator can edit this note.");
        var old=note.Text;note.Text=text.Trim();
        AtsService.Audit(db,uid,"Note edited",note.Id.ToString(),note.Text,note.CandidateId,note.AssessmentId,JsonSerializer.Serialize(new{Text=old}),JsonSerializer.Serialize(new{note.Text}));await db.SaveChangesAsync();
    }
    public async Task<List<AssessmentBatch>> Batches()
    {
        var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        return await db.AssessmentBatches.Include(x=>x.Items).AsNoTracking().Where(x=>x.OwnerId==uid).OrderByDescending(x=>x.CreatedUtc).Take(25).ToListAsync();
    }
    public async Task<Guid> CreateBatch(Guid positionId,string[] fileNames,Guid? sourceCandidateId=null,bool refreshProfile=false)
    {
        var uid=await access.UserId();if(refreshProfile&&sourceCandidateId is null)throw new InvalidOperationException("Select the existing candidate for an explicit profile refresh.");if(fileNames.Length is <1 or >5) throw new InvalidOperationException("Select between one and five CVs.");
        await using var db=await factory.CreateDbContextAsync();
        if(!await db.Positions.AnyAsync(x=>x.Id==positionId&&x.Status=="Active"&&x.CriteriaLocked)) throw new InvalidOperationException("Choose an active position with reviewed criteria.");
        if(sourceCandidateId is not null&&(fileNames.Length!=1||!await db.Candidates.AnyAsync(x=>x.Id==sourceCandidateId))) throw new InvalidOperationException("Choose one CV and an existing candidate for a profile refresh.");
        var batch=new AssessmentBatch{PositionId=positionId,OwnerId=uid,SourceCandidateId=sourceCandidateId,RefreshProfile=refreshProfile,Mode=fileNames.Length==1?"Single":"Batch",Items=fileNames.Select((name,index)=>new BatchItem{Index=index,FileName=Path.GetFileName(name)}).ToList()};
        db.AssessmentBatches.Add(batch);await db.SaveChangesAsync();return batch.Id;
    }
    public async Task SetBatchItem(Guid batchId,int index,Guid? assessmentId,string error="")
    {
        var uid=await access.UserId();await using var db=await factory.CreateDbContextAsync();
        var batch=await db.AssessmentBatches.Include(x=>x.Items).SingleAsync(x=>x.Id==batchId&&x.OwnerId==uid);
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
        if(batch.Items.Any(x=>x.State!="Saved")) throw new InvalidOperationException("Review failed/unsaved items before completing this batch.");
        batch.State="Completed";await db.SaveChangesAsync();
    }
}
