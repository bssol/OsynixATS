using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record SubmissionAttribution(string Submitter, string Source);

public sealed class RecruitmentAttributionService(IDbContextFactory<AtsDbContext> factory)
{
    static string SourceFor(string userId, string accountType, string workspaceOwnerId) =>
        userId != workspaceOwnerId && (accountType is AccountTypes.Freelance or AccountTypes.Employer) ? "External" : "Internal";

    public async Task<Dictionary<Guid, SubmissionAttribution>> Attribution(IEnumerable<Assessment> assessments)
    {
        var ids = assessments.Select(a => a.Id).Distinct().ToList();
        if (ids.Count == 0) return [];

        await using var db = await factory.CreateDbContextAsync();
        var records = await db.Assessments.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.OwnerId, a.PositionId }).ToListAsync();
        var submissions = await db.EmployerProfileSubmissions.AsNoTracking()
            .Where(s => ids.Contains(s.AssessmentId)).OrderByDescending(s => s.SubmittedUtc)
            .Select(s => new { s.AssessmentId, s.SubmittedBy, s.WorkspaceId }).ToListAsync();
        var submissionByAssessment = submissions.GroupBy(s => s.AssessmentId).ToDictionary(g => g.Key, g => g.First());
        var positionIds = records.Select(a => a.PositionId).Distinct().ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.WorkspaceId }).ToListAsync();
        var positionWorkspace = positions.ToDictionary(p => p.Id, p => p.WorkspaceId);
        var workspaceIds = submissions.Select(s => s.WorkspaceId)
            .Concat(positions.Where(p => p.WorkspaceId.HasValue).Select(p => p.WorkspaceId!.Value)).Distinct().ToList();
        var owners = await db.EmployerWorkspaces.AsNoTracking().Where(w => workspaceIds.Contains(w.Id))
            .Select(w => new { w.Id, w.OwnerId }).ToDictionaryAsync(w => w.Id, w => w.OwnerId);
        var userIds = records.Select(a => submissionByAssessment.TryGetValue(a.Id, out var submission) ? submission.SubmittedBy : a.OwnerId)
            .Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName, u.AccountType }).ToDictionaryAsync(u => u.Id);
        var result = new Dictionary<Guid, SubmissionAttribution>();

        foreach (var row in records)
        {
            var userId = submissionByAssessment.TryGetValue(row.Id, out var submission) ? submission.SubmittedBy : row.OwnerId;
            if (!users.TryGetValue(userId, out var user)) continue;
            var workspaceId = submission?.WorkspaceId ?? positionWorkspace.GetValueOrDefault(row.PositionId);
            var ownerId = workspaceId is Guid id && owners.TryGetValue(id, out var owner) ? owner : "";
            result[row.Id] = new(user.FullName.Length > 0 ? user.FullName : user.UserName ?? "Unknown",
                SourceFor(user.Id, user.AccountType, ownerId));
        }

        return result;
    }
}
