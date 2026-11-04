namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record HealthMetricResponse(
    Guid Id,
    string UserId,
    string Metric,
    double Value,
    string Unit,
    DateTimeOffset ObservedAt,
    DateTimeOffset CreatedAt,
    string Source,
    string ExternalId,
    string DeviceId,
    int SyncVersion,
    DateTimeOffset UpdatedAt);
