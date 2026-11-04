using AIFitnessCoach.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class UserConsentStore(FitnessDbContext dbContext) : IUserConsentStore
{
    public async Task<IReadOnlyList<AIFitnessCoach.Domain.Entities.UserConsent>> ListAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await dbContext.UserConsents.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);

    public Task<AIFitnessCoach.Domain.Entities.UserConsent?> GetAsync(
        string userId,
        string kind,
        CancellationToken cancellationToken) =>
        dbContext.UserConsents.AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Kind == kind, cancellationToken);

    public async Task<AIFitnessCoach.Domain.Entities.UserConsent> SetAsync(
        string userId,
        string kind,
        bool granted,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.UserConsents
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Kind == kind, cancellationToken);
        if (existing is null)
        {
            existing = AIFitnessCoach.Domain.Entities.UserConsent.Create(userId, kind, granted, version, now);
            dbContext.UserConsents.Add(existing);
        }
        else
        {
            existing.Update(granted, version, now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }
}

public sealed class DataRetentionStore(FitnessDbContext dbContext) : IDataRetentionStore
{
    public async Task<RetentionResult> PurgeAsync(
        RetentionPolicy policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var eventsBefore = now.AddDays(-policy.ProductEventsDays);
        var reportsBefore = now.AddDays(-policy.SupportReportsDays);
        var snapshotsBefore = now.AddDays(-policy.AIContextSnapshotsDays);
        var sessionsBefore = now.AddDays(-policy.EndedSessionsDays);

        if (dbContext.Database.IsRelational())
        {
            return new RetentionResult(
                await dbContext.ProductEvents.Where(item => item.OccurredAt < eventsBefore)
                    .ExecuteDeleteAsync(cancellationToken),
                await dbContext.SupportReports.Where(item => item.CreatedAt < reportsBefore)
                    .ExecuteDeleteAsync(cancellationToken),
                await dbContext.AIContextSnapshots.Where(item => item.CreatedAt < snapshotsBefore)
                    .ExecuteDeleteAsync(cancellationToken),
                // Refresh tokens of the session go with it (ON DELETE CASCADE).
                await dbContext.AuthSessions
                    .Where(item => (item.RevokedAt != null && item.RevokedAt < sessionsBefore)
                        || item.ExpiresAt < sessionsBefore)
                    .ExecuteDeleteAsync(cancellationToken));
        }

        // Providers without ExecuteDelete (tests).
        var events = await dbContext.ProductEvents.Where(item => item.OccurredAt < eventsBefore)
            .ToListAsync(cancellationToken);
        var reports = await dbContext.SupportReports.Where(item => item.CreatedAt < reportsBefore)
            .ToListAsync(cancellationToken);
        var snapshots = await dbContext.AIContextSnapshots.Where(item => item.CreatedAt < snapshotsBefore)
            .ToListAsync(cancellationToken);
        var sessions = await dbContext.AuthSessions
            .Where(item => (item.RevokedAt != null && item.RevokedAt < sessionsBefore)
                || item.ExpiresAt < sessionsBefore)
            .ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(item => item.Id).ToList();
        dbContext.RefreshTokens.RemoveRange(
            await dbContext.RefreshTokens.Where(item => sessionIds.Contains(item.AuthSessionId))
                .ToListAsync(cancellationToken));
        dbContext.ProductEvents.RemoveRange(events);
        dbContext.SupportReports.RemoveRange(reports);
        dbContext.AIContextSnapshots.RemoveRange(snapshots);
        dbContext.AuthSessions.RemoveRange(sessions);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return new RetentionResult(events.Count, reports.Count, snapshots.Count, sessions.Count);
    }
}
