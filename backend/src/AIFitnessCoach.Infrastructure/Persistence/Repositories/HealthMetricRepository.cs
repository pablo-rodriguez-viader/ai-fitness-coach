using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class HealthMetricRepository(FitnessDbContext dbContext) : IHealthMetricRepository
{
    public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    public async Task AddAsync(HealthMetric metric, CancellationToken cancellationToken)
    {
        await AddBatchAsync([metric], cancellationToken);
    }

    public async Task<HealthMetricBatchResult> AddBatchAsync(
        IReadOnlyList<HealthMetric> metrics,
        CancellationToken cancellationToken)
    {
        if (metrics.Count == 0)
            return new HealthMetricBatchResult(0, 0, 0);

        var userId = metrics[0].UserId;
        if (metrics.Any(metric => metric.UserId != userId))
            throw new ArgumentException("A health metric batch must belong to one user.", nameof(metrics));

        var keys = metrics
            .Select(metric => new { metric.Source, metric.ExternalId })
            .ToArray();
        var sources = keys.Select(key => key.Source).Distinct().ToArray();
        var externalIds = keys.Select(key => key.ExternalId).Distinct().ToArray();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await dbContext.HealthMetrics
                .Where(metric => metric.UserId == userId
                    && sources.Contains(metric.Source)
                    && externalIds.Contains(metric.ExternalId))
                .ToListAsync(cancellationToken);
            var byKey = existing.ToDictionary(
                metric => $"{metric.Source}\u001f{metric.ExternalId}",
                StringComparer.Ordinal);
            var inserted = 0;
            var updated = 0;
            var additions = new List<HealthMetric>();
            foreach (var metric in metrics)
            {
                if (byKey.TryGetValue($"{metric.Source}\u001f{metric.ExternalId}", out var current))
                {
                    if (current.UpdateFromSync(
                        metric.Value,
                        metric.Unit,
                        metric.ObservedAt,
                        metric.DeviceId,
                        metric.UpdatedAt))
                    {
                        updated++;
                    }
                }
                else
                {
                    additions.Add(metric);
                    byKey.Add($"{metric.Source}\u001f{metric.ExternalId}", metric);
                    inserted++;
                }
            }

            if (inserted == 0 && updated == 0)
                return new HealthMetricBatchResult(0, 0, metrics.Count);

            dbContext.HealthMetrics.AddRange(additions);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return new HealthMetricBatchResult(inserted, updated, metrics.Count - inserted - updated);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                HealthSyncTelemetry.RecordRetry();
                foreach (var metric in additions)
                    dbContext.Entry(metric).State = EntityState.Detached;
                foreach (var metric in existing)
                    dbContext.Entry(metric).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Health metrics could not be persisted after a concurrent update.");
    }

    public async Task<int> DeleteBatchAsync(
        string userId,
        string source,
        IReadOnlyList<string> externalIds,
        CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0)
            return 0;

        var matches = await dbContext.HealthMetrics
            .Where(metric => metric.UserId == userId
                && metric.Source == source
                && externalIds.Contains(metric.ExternalId))
            .ToListAsync(cancellationToken);
        dbContext.HealthMetrics.RemoveRange(matches);
        await dbContext.SaveChangesAsync(cancellationToken);
        return matches.Count;
    }

    public async Task<IReadOnlyList<HealthMetric>> GetPageAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        HealthMetricCursor? before,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId
                && metric.ObservedAt >= from
                && metric.ObservedAt <= to);

        if (before is not null)
        {
            query = query.Where(metric => metric.ObservedAt < before.ObservedAt
                || (metric.ObservedAt == before.ObservedAt && metric.Id.CompareTo(before.Id) < 0));
        }

        return await query
            .OrderByDescending(metric => metric.ObservedAt)
            .ThenByDescending(metric => metric.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HealthMetric>> GetLatestByMetricAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId)
            .GroupBy(metric => metric.Metric)
            .Select(group => group
                .OrderByDescending(metric => metric.ObservedAt)
                .ThenByDescending(metric => metric.Id)
                .First())
            .ToListAsync(cancellationToken);
    }

    public async Task<HealthMetricSummary> GetSummaryAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var day = dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId
                && metric.ObservedAt >= from
                && metric.ObservedAt <= to);

        var steps = await day.Where(metric => metric.Metric == "steps")
            .SumAsync(metric => (double?)metric.Value, cancellationToken);
        var activeEnergy = await day.Where(metric => metric.Metric == "active_energy")
            .SumAsync(metric => (double?)metric.Value, cancellationToken);
        var basalEnergy = await day.Where(metric => metric.Metric == "basal_energy")
            .SumAsync(metric => (double?)metric.Value, cancellationToken);
        var hasTotalEnergySamples = await day.AnyAsync(metric => metric.Metric == "total_energy", cancellationToken);
        var totalEnergySamples = await day.Where(metric => metric.Metric == "total_energy")
            .SumAsync(metric => (double?)metric.Value, cancellationToken);
        var heartRate = await day.Where(metric => metric.Metric == "heart_rate")
            .AverageAsync(metric => (double?)metric.Value, cancellationToken);
        var sleep = await day.Where(metric => metric.Metric == "sleep_duration")
            .SumAsync(metric => (double?)metric.Value, cancellationToken);
        var latestWeight = await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId && metric.Metric == "body_mass")
            .OrderByDescending(metric => metric.ObservedAt)
            .Select(metric => (double?)metric.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return new HealthMetricSummary(
            steps,
            latestWeight,
            activeEnergy,
            hasTotalEnergySamples ? totalEnergySamples : (activeEnergy.HasValue || basalEnergy.HasValue
                ? activeEnergy.GetValueOrDefault() + basalEnergy.GetValueOrDefault()
                : null),
            heartRate,
            sleep);
    }
}
