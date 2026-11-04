using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed class CreateHealthMetricUseCase(
    IHealthMetricRepository healthMetricRepository,
    TimeProvider timeProvider)
{
    public async Task<HealthMetric> ExecuteAsync(
        string userId,
        CreateHealthMetricCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "A valid authenticated user is required.");
        if (string.IsNullOrWhiteSpace(command.Metric))
            throw UseCaseValidationException.For("metric", "Metric is required.");
        if (string.IsNullOrWhiteSpace(command.Unit))
            throw UseCaseValidationException.For("unit", "Unit is required.");

        if (!await healthMetricRepository.UserExistsAsync(userId, cancellationToken))
            throw UseCaseValidationException.For("userId", "The authenticated user does not exist.");

        var now = timeProvider.GetUtcNow();
        if (command.ObservedAt > now.AddMinutes(5))
            throw UseCaseValidationException.For("observedAt", "Observed time cannot be more than five minutes in the future.");

        HealthMetric metric;
        try
        {
            metric = HealthMetric.Create(
                Guid.NewGuid(),
                userId,
                command.Metric,
                command.Value,
                command.Unit,
                command.ObservedAt,
                now,
                command.Source ?? string.Empty,
                command.ExternalId,
                command.DeviceId ?? "unknown-device");
        }
        catch (ArgumentException exception)
        {
            throw UseCaseValidationException.For("metric", exception.Message);
        }

        await healthMetricRepository.AddAsync(metric, cancellationToken);
        return metric;
    }
}
