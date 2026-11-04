using System.Text.Json;
using AIFitnessCoach.AI.Models;

using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class ProductAnalyticsRepository(FitnessDbContext dbContext) : IProductAnalyticsRepository
{
    /// <summary>Upper bound for one report; the beta has far fewer events.</summary>
    private const int MaximumEvents = 500_000;

    public async Task AddEventsAsync(IReadOnlyList<ProductEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
            return;
        dbContext.ProductEvents.AddRange(events);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var passive = PassiveEventNames;
        var rows = await dbContext.ProductEvents
            .AsNoTracking()
            .Where(item => item.OccurredAt >= from && item.OccurredAt <= to && !passive.Contains(item.Name))
            .OrderBy(item => item.OccurredAt)
            .Take(MaximumEvents)
            .Select(item => new { item.UserId, item.Name, item.OccurredAt, item.PropertiesJson, item.AppVersion })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new AnalyticsEvent(
                row.UserId, row.Name, row.OccurredAt, Parse(row.PropertiesJson), row.AppVersion))
            .ToArray();
    }

    public async Task<IReadOnlyList<AnalyticsEvent>> GetPassiveEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var passive = PassiveEventNames;
        var rows = await dbContext.ProductEvents
            .AsNoTracking()
            .Where(item => item.OccurredAt >= from && item.OccurredAt <= to && passive.Contains(item.Name))
            .OrderBy(item => item.OccurredAt)
            .Take(MaximumEvents)
            .Select(item => new { item.UserId, item.Name, item.OccurredAt, item.PropertiesJson, item.AppVersion })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new AnalyticsEvent(
                row.UserId, row.Name, row.OccurredAt, Parse(row.PropertiesJson), row.AppVersion))
            .ToArray();
    }

    public Task<int> CountUsersAsync(CancellationToken cancellationToken) =>
        dbContext.Users.CountAsync(cancellationToken);

    private static readonly string[] PassiveEventNames = ProductEventNames.PassiveEvents.ToArray();

    public async Task<IReadOnlyList<UserActivitySnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<string> userIds,
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return [];
        var fromDay = DateOnly.FromDateTime(from.UtcDateTime);

        var passive = PassiveEventNames;
        var events = await dbContext.ProductEvents
            .AsNoTracking()
            .Where(item => ids.Contains(item.UserId) && item.OccurredAt >= from && !passive.Contains(item.Name))
            .Select(item => new { item.UserId, item.Name, item.OccurredAt, item.PropertiesJson })
            .ToListAsync(cancellationToken);

        var workoutDays = await (
                from day in dbContext.WorkoutDays.AsNoTracking()
                join plan in dbContext.WorkoutPlans.AsNoTracking() on day.WorkoutPlanId equals plan.Id
                where ids.Contains(plan.UserId) && day.Date >= fromDay
                select new { plan.UserId, day.Id, day.Date, day.Title })
            .ToListAsync(cancellationToken);
        var completions = await CompletionTimesAsync(ids, from.AddDays(-7), cancellationToken);

        var nutritionDays = await dbContext.NutritionLogs
            .AsNoTracking()
            .Where(log => ids.Contains(log.UserId) && log.Date >= fromDay)
            .Select(log => new { log.UserId, log.Date })
            .ToListAsync(cancellationToken);

        var goals = await dbContext.Goals
            .AsNoTracking()
            .Where(goal => ids.Contains(goal.UserId))
            .Select(goal => new { goal.UserId, goal.Id, goal.Type, goal.Status, goal.StartsOn, goal.CreatedAt, goal.UpdatedAt })
            .ToListAsync(cancellationToken);

        var steps = await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => ids.Contains(metric.UserId) && metric.Metric == "steps" && metric.ObservedAt >= from)
            .Select(metric => new { metric.UserId, metric.ObservedAt, metric.Value })
            .ToListAsync(cancellationToken);

        return ids.Select(userId =>
            {
                var userCompletions = completions.GetValueOrDefault(userId) ?? new Dictionary<Guid, DateTimeOffset>();
                return new UserActivitySnapshot(
                    userId,
                    events.Where(item => item.UserId == userId)
                        .Select(item => new AnalyticsEvent(item.UserId, item.Name, item.OccurredAt, Parse(item.PropertiesJson)))
                        .ToArray(),
                    workoutDays.Where(day => day.UserId == userId)
                        .Select(day => new WorkoutDayFact(
                            day.Id,
                            day.Date,
                            day.Title,
                            userCompletions.TryGetValue(day.Id, out var completedAt) ? completedAt : null))
                        .ToArray(),
                    nutritionDays.Where(log => log.UserId == userId).Select(log => log.Date).ToArray(),
                    goals.Where(goal => goal.UserId == userId)
                        .Select(goal => new GoalFact(
                            goal.Id,
                            goal.Type.ToString(),
                            goal.Status.ToString(),
                            goal.StartsOn,
                            goal.CreatedAt,
                            goal.UpdatedAt))
                        .ToArray(),
                    steps.Where(metric => metric.UserId == userId)
                        .GroupBy(metric => DateOnly.FromDateTime(metric.ObservedAt.UtcDateTime))
                        .ToDictionary(group => group.Key, group => group.Sum(metric => metric.Value)));
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<RecommendationFact>> GetRecommendationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        await dbContext.WorkoutRecommendations
            .AsNoTracking()
            .Where(item => item.CreatedAt >= from && item.CreatedAt <= to)
            .Select(item => new RecommendationFact(item.Id, item.UserId, item.Type, item.CreatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>>> GetCompletedWorkoutsAsync(
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        var completions = await CompletionTimesAsync(null, from, cancellationToken);
        return completions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DateTimeOffset>)pair.Value.Values.Order().ToArray(),
            StringComparer.Ordinal);
    }

    public async Task<int> CompleteReachedGoalsAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var goals = await dbContext.Goals
            .Include(goal => goal.Progress)
            .Where(goal => goal.UserId == userId && goal.Status == GoalStatus.Active && goal.TargetWeightKilograms != null)
            .ToListAsync(cancellationToken);
        if (goals.Count == 0)
            return 0;
        var latestWeight = await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId && metric.Metric == "body_mass")
            .OrderByDescending(metric => metric.ObservedAt)
            .Select(metric => new { metric.Value, metric.ObservedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (latestWeight is null)
            return 0;

        var completed = 0;
        foreach (var goal in goals)
        {
            // Needs a recorded starting weight and a newer measurement; without them there is
            // no direction to judge (GoalAnalyzer reports 100 % for a goal that has not started).
            var start = goal.Progress.OrderBy(progress => progress.Date).FirstOrDefault()?.WeightKilograms;
            var target = goal.TargetWeightKilograms!.Value;
            if (start is null
                || latestWeight.ObservedAt <= goal.CreatedAt
                || Math.Abs(start.Value - target) < 0.1)
                continue;
            // Reached once the weight crosses the target in the intended direction; overshooting
            // still counts (GoalAnalyzer's distance-based percentage drops below 100 then).
            var reached = start.Value > target
                ? latestWeight.Value <= target
                : latestWeight.Value >= target;
            if (reached)
            {
                goal.SetStatus(GoalStatus.Completed, now);
                dbContext.ProductEvents.Add(ProductEvent.Create(
                    userId,
                    ProductEventNames.GoalCompleted,
                    now,
                    now,
                    new Dictionary<string, string> { ["type"] = goal.Type.ToString() }));
                completed++;
            }
        }
        if (completed > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
        return completed;
    }

    /// <summary>Earliest completion per workout day, from explicit completions and "completed" feedback.</summary>
    private async Task<Dictionary<string, Dictionary<Guid, DateTimeOffset>>> CompletionTimesAsync(
        string[]? userIds,
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        var completionsQuery = dbContext.WorkoutCompletions.AsNoTracking()
            .Where(item => item.Completed && item.RecordedAt >= from);
        var feedbackQuery = dbContext.WorkoutFeedback.AsNoTracking()
            .Where(item => item.Completed && item.CreatedAt >= from);
        if (userIds is not null)
        {
            completionsQuery = completionsQuery.Where(item => userIds.Contains(item.UserId));
            feedbackQuery = feedbackQuery.Where(item => userIds.Contains(item.UserId));
        }
        var completions = await completionsQuery
            .Select(item => new { item.UserId, item.WorkoutDayId, At = item.RecordedAt })
            .ToListAsync(cancellationToken);
        var feedback = await feedbackQuery
            .Select(item => new { item.UserId, item.WorkoutDayId, At = item.CreatedAt })
            .ToListAsync(cancellationToken);
        return completions.Concat(feedback)
            .GroupBy(item => item.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(item => item.WorkoutDayId)
                    .ToDictionary(day => day.Key, day => day.Min(item => item.At)),
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}

public sealed class ProductEventRecorder(
    FitnessDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<ProductEventRecorder> logger) : IProductEventRecorder
{
    public async Task RecordAsync(
        string userId,
        string name,
        IReadOnlyDictionary<string, string>? properties,
        CancellationToken cancellationToken)
    {
        ProductEvent? productEvent = null;
        try
        {
            var now = timeProvider.GetUtcNow();
            productEvent = ProductEvent.Create(userId, name, now, now, properties);
            dbContext.ProductEvents.Add(productEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (productEvent is not null)
                dbContext.Entry(productEvent).State = EntityState.Detached;
            logger.LogWarning(exception, "Product event {EventName} could not be recorded.", name);
        }
    }
}
