using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed class GetLatestHealthMetricsUseCase(IHealthMetricRepository repository)
{
    public async Task<IReadOnlyList<HealthMetric>> ExecuteAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            throw UseCaseValidationException.For("userId", "The authenticated user does not exist.");

        return await repository.GetLatestByMetricAsync(userId, cancellationToken);
    }
}
