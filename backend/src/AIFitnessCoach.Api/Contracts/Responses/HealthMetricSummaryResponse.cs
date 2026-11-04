namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record HealthMetricSummaryResponse(
    double? Steps,
    double? WeightKilograms,
    double? ActiveEnergyKilocalories,
    double? TotalEnergyKilocalories,
    double? AverageHeartRateBeatsPerMinute,
    double? SleepSeconds);

public sealed record HealthMetricBatchResponse(
    int ReceivedCount,
    int InsertedCount,
    int UpdatedCount,
    int DuplicateCount,
    int DeletedCount,
    int RejectedCount = 0);
