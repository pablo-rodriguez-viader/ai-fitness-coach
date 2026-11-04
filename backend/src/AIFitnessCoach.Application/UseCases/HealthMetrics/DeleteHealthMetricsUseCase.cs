using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed class DeleteHealthMetricsUseCase(IHealthMetricRepository repository)
{
    public async Task<int> ExecuteAsync(
        string userId,
        DeleteHealthMetricsCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        if (command.ExternalIds.Count is < 1 or > 500)
            throw UseCaseValidationException.For(
                "externalIds",
                "A deletion batch must contain between 1 and 500 external ids.");
        if (string.IsNullOrWhiteSpace(command.Source) || command.Source.Length > 64)
            throw UseCaseValidationException.For("source", "A valid metric source is required.");
        if (string.IsNullOrWhiteSpace(command.DeviceId) || command.DeviceId.Length > 128)
            throw UseCaseValidationException.For("deviceId", "A valid device id is required.");
        if (command.ExternalIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            throw UseCaseValidationException.For(
                "externalIds",
                "Each external id must contain between 1 and 128 characters.");
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            throw UseCaseValidationException.For("userId", "The authenticated user does not exist.");

        return await repository.DeleteBatchAsync(
            userId,
            command.Source,
            command.ExternalIds.Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
    }
}
