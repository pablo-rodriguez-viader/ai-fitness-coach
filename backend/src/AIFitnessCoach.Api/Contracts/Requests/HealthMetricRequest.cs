namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record HealthMetricRequest(
    string? Metric,
    double Value,
    string? Unit,
    DateTimeOffset ObservedAt,
    string? Source,
    string? SourceRecordId,
    string? DeviceId = null,
    string? ExternalId = null);
