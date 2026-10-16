using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public interface ICvProfileExtractionQueue
{
    void Queue(Guid candidateId);
}

public sealed class CvProfileExtractionQueue(IServiceScopeFactory scopes,IDbContextFactory<AtsDbContext> factory,RecruitmentUpdates updates,ILogger<CvProfileExtractionQueue> logger):BackgroundService,ICvProfileExtractionQueue
{
    readonly Channel<Guid> queue=Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions{SingleReader=true,SingleWriter=false});

    public void Queue(Guid candidateId)=>queue.Writer.TryWrite(candidateId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try{
            await using var db=await factory.CreateDbContextAsync(stoppingToken);
            foreach(var id in await db.Candidates.AsNoTracking().Where(c=>c.CvProfileStatus=="Pending").Select(c=>c.Id).ToListAsync(stoppingToken))queue.Writer.TryWrite(id);
        }catch(Exception ex)when(!stoppingToken.IsCancellationRequested){logger.LogError(ex,"Could not recover pending candidate CV profile extractions.");}

        await foreach(var candidateId in queue.Reader.ReadAllAsync(stoppingToken))await Process(candidateId,stoppingToken);
    }

    async Task Process(Guid candidateId,CancellationToken token)
    {
        try{
            Guid? documentId;
            string fileName;
            await using(var db=await factory.CreateDbContextAsync(token)){
                var candidate=await db.Candidates.AsNoTracking().Where(c=>c.Id==candidateId).Select(c=>new{c.CvProfileStatus,c.DocumentId,c.Document!.FileName}).SingleOrDefaultAsync(token);
                if(candidate is null||candidate.CvProfileStatus!="Pending")return;
                documentId=candidate.DocumentId;fileName=candidate.FileName;
            }
            if(documentId is null)throw new InvalidOperationException("The original CV is unavailable. The application remains saved for manual review.");
            using var scope=scopes.CreateScope();var documents=scope.ServiceProvider.GetRequiredService<IDocumentStore>();var document=await documents.Read(documentId.Value,token);
            if(document is null)throw new InvalidOperationException("The original CV is unavailable. The application remains saved for manual review.");
            await scope.ServiceProvider.GetRequiredService<AiService>().ExtractPublicCvProfile(candidateId,fileName,document.Content,token);
        }catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
        catch(Exception ex){
            try{
                await using var db=await factory.CreateDbContextAsync(token);var candidate=await db.Candidates.SingleOrDefaultAsync(c=>c.Id==candidateId,token);if(candidate is null)return;
                candidate.CvProfileStatus="Failed";candidate.CvProfileError=(ex is InvalidOperationException?ex.Message:"CV Profile Extraction Failed. The Original CV Is Saved; Review It Manually.").Trim();if(candidate.CvProfileError.Length>500)candidate.CvProfileError=candidate.CvProfileError[..500];
                AtsService.Audit(db,"Public Careers Page","Candidate CV profile extraction failed",candidate.Id.ToString(),candidate.CvProfileError,candidate.Id);await db.SaveChangesAsync(token);
            }catch(Exception saveError)when(!token.IsCancellationRequested){logger.LogError(saveError,"Could not save CV extraction failure for candidate {CandidateId}.",candidateId);}
            logger.LogWarning(ex,"CV profile extraction failed for candidate {CandidateId}.",candidateId);
        }
        finally{if(!token.IsCancellationRequested)updates.Publish();}
    }
}
