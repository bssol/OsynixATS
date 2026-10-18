using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;

namespace Osynix.Ats.Services;

public interface IPublicApplicationAssessmentQueue
{
    void Queue(Guid applicationId);
}

public sealed class PublicApplicationAssessmentQueue(IServiceScopeFactory scopes, IDbContextFactory<AtsDbContext> factory,
    RecruitmentUpdates updates, ILogger<PublicApplicationAssessmentQueue> logger) : BackgroundService, IPublicApplicationAssessmentQueue
{
    readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Queue(Guid applicationId) => queue.Writer.TryWrite(applicationId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var db = await factory.CreateDbContextAsync(stoppingToken))
            foreach (var id in await db.Assessments.AsNoTracking().Where(a => a.AutoAssessmentStatus == "Pending").Select(a => a.Id).ToListAsync(stoppingToken))
                Queue(id);

        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AiService>().AssessPublicApplication(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Automatic assessment failed for public application {ApplicationId}.", id);
                try
                {
                    await using var db = await factory.CreateDbContextAsync(stoppingToken);
                    var application = await db.Assessments.Include(a => a.Candidate).SingleOrDefaultAsync(a => a.Id == id, stoppingToken);
                    if (application?.AutoAssessmentStatus != "Pending") continue;
                    var message = ex is InvalidOperationException ? ex.Message : "Automatic assessment could not be completed. The application and original CV are saved for review.";
                    application.AutoAssessmentStatus = "Failed";
                    application.AutoAssessmentError = message.Length > 500 ? message[..500] : message;
                    if (application.Candidate is { } candidate && candidate.DocumentId == application.DocumentId && candidate.CvProfileStatus == "Pending")
                    {
                        candidate.CvProfileStatus = "Failed";
                        candidate.CvProfileError = application.AutoAssessmentError;
                    }
                    AtsService.Audit(db, "Public Careers Page", "Automatic application assessment failed", id.ToString(), application.AutoAssessmentError, application.CandidateId, id);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception saveError) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(saveError, "Could not save automatic assessment failure for {ApplicationId}.", id);
                }
            }
            finally { if (!stoppingToken.IsCancellationRequested) updates.Publish(); }
        }
    }
}
