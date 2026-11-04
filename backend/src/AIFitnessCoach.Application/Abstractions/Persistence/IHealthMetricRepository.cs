using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Application.UseCases.HealthMetrics;

namespace AIFitnessCoach.Application.Abstractions.Persistence;

public interface IHealthMetricRepository
{
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken);
    Task AddAsync(HealthMetric metric, CancellationToken cancellationToken);
    Task<HealthMetricBatchResult> AddBatchAsync(
        IReadOnlyList<HealthMetric> metrics,
        CancellationToken cancellationToken);
    Task<int> DeleteBatchAsync(
        string userId,
        string source,
        IReadOnlyList<string> externalIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<HealthMetric>> GetPageAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        HealthMetricCursor? before,
        int pageSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<HealthMetric>> GetLatestByMetricAsync(
        string userId,
        CancellationToken cancellationToken);
    Task<HealthMetricSummary> GetSummaryAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
