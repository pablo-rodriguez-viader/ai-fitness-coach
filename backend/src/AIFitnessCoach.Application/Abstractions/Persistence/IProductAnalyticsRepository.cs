using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.Abstractions.Persistence;

public interface IProductAnalyticsRepository
{
    Task AddEventsAsync(IReadOnlyList<ProductEvent> events, CancellationToken cancellationToken);

    /// <summary>Events of every user between the two instants (inclusive), without passive
    /// events (<see cref="ProductEventNames.PassiveEvents"/>): these define who is active.</summary>
    Task<IReadOnlyList<AnalyticsEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>Sync, crash, hang and error events between the two instants, with app version.</summary>
    Task<IReadOnlyList<AnalyticsEvent>> GetPassiveEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>Registered accounts, for the beta capacity.</summary>
    Task<int> CountUsersAsync(CancellationToken cancellationToken);

    /// <summary>Loads events, workouts, meals, goals and steps since <paramref name="from"/>.</summary>
    Task<IReadOnlyList<UserActivitySnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<string> userIds,
        DateTimeOffset from,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationFact>> GetRecommendationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>Completed-workout times per user since <paramref name="from"/>.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>>> GetCompletedWorkoutsAsync(
        DateTimeOffset from,
        CancellationToken cancellationToken);

    /// <summary>Marks active weight goals whose target was reached as completed; returns how many.</summary>
    Task<int> CompleteReachedGoalsAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Records a product event for an action that just succeeded. Never throws: analytics
/// must not break the user's request.</summary>
public interface IProductEventRecorder
{
    Task RecordAsync(
        string userId,
        string name,
        IReadOnlyDictionary<string, string>? properties,
        CancellationToken cancellationToken);
}
