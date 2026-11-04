using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed class GetHealthSummaryUseCase(
    IHealthMetricRepository repository,
    TimeProvider timeProvider)
{
    public async Task<HealthMetricSummary> ExecuteAsync(
        string userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            throw UseCaseValidationException.For("userId", "The authenticated user does not exist.");

        var end = to ?? timeProvider.GetUtcNow();
        var start = from ?? end.Date;
        if (start >= end || end - start > TimeSpan.FromDays(2))
            throw UseCaseValidationException.For(
                "range",
                "A health summary range must be positive and no longer than two days.");

        return await repository.GetSummaryAsync(userId, start, end, cancellationToken);
    }
}
