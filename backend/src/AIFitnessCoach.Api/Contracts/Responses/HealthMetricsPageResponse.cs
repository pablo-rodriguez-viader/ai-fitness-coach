namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record HealthMetricsPageResponse(
    IReadOnlyList<HealthMetricResponse> Items,
    string? NextCursor,
    DateTimeOffset From,
    DateTimeOffset To);
