using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public sealed class GetHealthMetricsUseCase(
    IHealthMetricRepository repository,
    TimeProvider timeProvider)
{
    public async Task<HealthMetricsPage> ExecuteAsync(
        string userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        HealthMetricCursor? before,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "A valid authenticated user is required.");

        var end = to ?? timeProvider.GetUtcNow();
        var start = from ?? end.AddDays(-30);
        if (start > end || end - start > TimeSpan.FromDays(366))
            throw UseCaseValidationException.For("range", "The requested range must be ordered and no longer than 366 days.");
        if (pageSize is < 1 or > 500)
            throw UseCaseValidationException.For("pageSize", "Page size must be between 1 and 500.");
        if (before is not null && (before.ObservedAt < start || before.ObservedAt > end))
            throw UseCaseValidationException.For("cursor", "Cursor must fall inside the requested date range.");

        var rows = await repository.GetPageAsync(
            userId,
            start,
            end,
            before,
            pageSize + 1,
            cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.Take(pageSize).ToArray() : rows.ToArray();
        return new HealthMetricsPage(
            items,
            hasMore ? new HealthMetricCursor(items[^1].ObservedAt, items[^1].Id) : null,
            start,
            end);
    }
}
