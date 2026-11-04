namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record SyncHealthMetricsRequest(
    IReadOnlyList<HealthMetricRequest>? Metrics,
    string? UserId = null,
    IReadOnlyList<HealthMetricDeletionRequest>? DeletedMetrics = null);

public sealed record HealthMetricDeletionRequest(string? Source, string? ExternalId, string? DeviceId);
