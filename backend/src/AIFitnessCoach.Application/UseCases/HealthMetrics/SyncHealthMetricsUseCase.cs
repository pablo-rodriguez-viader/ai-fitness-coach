using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed class SyncHealthMetricsUseCase(
    IHealthMetricRepository healthMetricRepository,
    TimeProvider timeProvider)
{
    /// <param name="skipInvalidItems">
    /// When true, samples that fail per-item validation are skipped and counted in
    /// <see cref="HealthMetricBatchResult.RejectedCount"/> instead of failing the whole batch.
    /// Device sync uses this so one bad sample cannot block a queue of valid ones.
    /// </param>
    public async Task<HealthMetricBatchResult> ExecuteAsync(
        string userId,
        SyncHealthMetricsCommand command,
        CancellationToken cancellationToken,
        bool skipInvalidItems = false)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        if (command.Metrics.Count is < 1 or > 500)
            throw UseCaseValidationException.For("metrics", "A batch must contain between 1 and 500 metrics.");
        if (!await healthMetricRepository.UserExistsAsync(userId, cancellationToken))
            throw UseCaseValidationException.For("userId", "The authenticated user does not exist.");

        var now = timeProvider.GetUtcNow();
        var metrics = new List<HealthMetric>(command.Metrics.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var duplicateCount = 0;
        var rejectedCount = 0;
        for (var index = 0; index < command.Metrics.Count; index++)
        {
            var item = command.Metrics[index];
            if (item.ObservedAt > now.AddMinutes(5))
            {
                if (skipInvalidItems)
                {
                    rejectedCount++;
                    continue;
                }
                throw UseCaseValidationException.For(
                    $"metrics[{index}].observedAt",
                    "Observed time cannot be more than five minutes in the future.");
            }
            if (string.IsNullOrWhiteSpace(item.DeviceId))
            {
                if (skipInvalidItems)
                {
                    rejectedCount++;
                    continue;
                }
                throw UseCaseValidationException.For(
                    $"metrics[{index}].deviceId",
                    "A device id is required for synchronized metrics.");
            }

            try
            {
                var metric = HealthMetric.Create(
                    Guid.NewGuid(),
                    userId,
                    item.Metric ?? string.Empty,
                    item.Value,
                    item.Unit ?? string.Empty,
                    item.ObservedAt,
                    now,
                    item.Source ?? string.Empty,
                    item.ExternalId,
                    item.DeviceId);
                var key = $"{metric.Source}\u001f{metric.ExternalId}";
                if (!keys.Add(key))
                {
                    duplicateCount++;
                    continue;
                }
                metrics.Add(metric);
            }
            catch (ArgumentException exception)
            {
                if (skipInvalidItems)
                {
                    rejectedCount++;
                    continue;
                }
                throw UseCaseValidationException.For($"metrics[{index}]", exception.Message);
            }
        }

        var result = metrics.Count == 0
            ? new HealthMetricBatchResult(0, 0, 0)
            : await healthMetricRepository.AddBatchAsync(metrics, cancellationToken);
        return result with
        {
            DuplicateCount = result.DuplicateCount + duplicateCount,
            RejectedCount = rejectedCount
        };
    }
}
