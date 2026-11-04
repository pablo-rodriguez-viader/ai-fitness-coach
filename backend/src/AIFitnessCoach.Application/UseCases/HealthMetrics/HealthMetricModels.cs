using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed record CreateHealthMetricCommand(
    string? Metric,
    double Value,
    string? Unit,
    DateTimeOffset ObservedAt,
    string? Source = "manual",
    string? ExternalId = null,
    string? DeviceId = null);

public sealed record SyncHealthMetricsCommand(IReadOnlyList<CreateHealthMetricCommand> Metrics);
public sealed record HealthMetricBatchResult(
    int InsertedCount,
    int UpdatedCount,
    int DuplicateCount,
    int RejectedCount = 0);

public sealed record HealthMetricSummary(
    double? Steps,
    double? WeightKilograms,
    double? ActiveEnergyKilocalories,
    double? TotalEnergyKilocalories,
    double? AverageHeartRateBeatsPerMinute,
    double? SleepSeconds);

public sealed record DeleteHealthMetricsCommand(
    string? Source,
    IReadOnlyList<string> ExternalIds,
    string? DeviceId);

public sealed record HealthMetricCursor(DateTimeOffset ObservedAt, Guid Id);

public sealed record HealthMetricsPage(
    IReadOnlyList<HealthMetric> Items,
    HealthMetricCursor? NextCursor,
    DateTimeOffset From,
    DateTimeOffset To);
