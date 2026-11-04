using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.Abstractions.Persistence;

/// <summary>Rows of one table in an account export: column name → value.</summary>
public sealed record AccountExportTable(string Name, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

/// <summary>Everything stored about one user, for data portability and account deletion.</summary>
public interface IAccountDataStore
{
    /// <summary>Every table that belongs to the user, or null when the user does not exist.</summary>
    Task<IReadOnlyList<AccountExportTable>?> ExportAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Deletes the user and every row that belongs to them. Returns false if the user does not exist.</summary>
    Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes what was sent to or produced by the AI provider: conversations, messages, summaries,
    /// context snapshots, feedback and request records. The consent row is kept (as revoked) as
    /// proof of the withdrawal. Returns the number of conversations deleted.
    /// </summary>
    Task<int> DeleteAIDataAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Deletes every synced Apple Health sample of the user. Returns the number deleted.</summary>
    Task<int> DeleteHealthDataAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>How long each category is kept (GDPR storage limitation). Account data itself is
/// kept while the account exists and deleted with it.</summary>
public sealed record RetentionPolicy(
    int ProductEventsDays,
    int SupportReportsDays,
    int AIContextSnapshotsDays,
    int EndedSessionsDays);

public sealed record RetentionResult(
    int ProductEvents,
    int SupportReports,
    int AIContextSnapshots,
    int EndedSessions);

public interface IDataRetentionStore
{
    /// <summary>Deletes rows older than the policy allows, for every user.</summary>
    Task<RetentionResult> PurgeAsync(RetentionPolicy policy, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IUserConsentStore
{
    Task<IReadOnlyList<UserConsent>> ListAsync(string userId, CancellationToken cancellationToken);
    Task<UserConsent?> GetAsync(string userId, string kind, CancellationToken cancellationToken);
    Task<UserConsent> SetAsync(string userId, string kind, bool granted, int version, DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ISupportReportRepository
{
    Task AddAsync(SupportReport report, CancellationToken cancellationToken);

    /// <summary>Newest first, for the team's triage.</summary>
    Task<IReadOnlyList<SupportReport>> ListAsync(
        string? kind,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Reports sent by the user since the given instant, used for abuse limits.</summary>
    Task<int> CountSinceAsync(string userId, DateTimeOffset since, CancellationToken cancellationToken);
}
