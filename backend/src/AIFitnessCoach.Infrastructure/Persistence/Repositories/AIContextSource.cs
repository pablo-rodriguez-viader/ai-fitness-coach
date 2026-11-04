using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class AIContextSource(FitnessDbContext dbContext) : IAIContextSource
{
    public async Task<AIUserContext> GetContextAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.DisplayName, item.Age, item.HeightCentimeters, item.Goal })
            .SingleOrDefaultAsync(cancellationToken);
        var metrics = new List<AIHealthMetricContext>();
        foreach (var metricType in new[]
                 {
                     "steps",
                     "body_mass",
                     "sleep_duration",
                     "heart_rate",
                     "resting_heart_rate",
                     "active_energy",
                     "basal_energy",
                     "total_energy",
                     "workout_duration",
                     "workout_distance"
                 })
        {
            var recentMetrics = await dbContext.HealthMetrics
                .AsNoTracking()
                .Where(metric => metric.UserId == userId
                    && metric.Metric == metricType
                    && metric.ObservedAt >= from)
                .OrderByDescending(metric => metric.ObservedAt)
                .Take(30)
                .Select(metric => new AIHealthMetricContext(
                    metric.Metric,
                    metric.Value,
                    metric.Unit,
                    metric.ObservedAt))
                .ToArrayAsync(cancellationToken);
            metrics.AddRange(recentMetrics);
        }

        var nutritionProfile = await dbContext.NutritionProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var activeGoal = await dbContext.Goals
            .AsNoTracking()
            .Where(goal => goal.UserId == userId && goal.Status == GoalStatus.Active)
            .OrderByDescending(goal => goal.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var currentWeight = await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId && metric.Metric == "body_mass")
            .OrderByDescending(metric => metric.ObservedAt)
            .Select(metric => (double?)metric.Value)
            .FirstOrDefaultAsync(cancellationToken);
        AIUserNutritionContext? nutritionContext = null;
        if (profile is { Age: >= 18 }
            && nutritionProfile is not null
            && profile.HeightCentimeters is >= 100 and <= 300
            && currentWeight is >= 20 and <= 500)
        {
            var weight = currentWeight.Value;
            var goalType = activeGoal?.Type ?? (GoalTypeParser.TryParse(profile.Goal, out var parsedGoal)
                ? parsedGoal
                : GoalType.GeneralHealth);
            var nutritionTarget = new MetabolicAnalyzer().Calculate(
                weight,
                profile.HeightCentimeters,
                profile.Age,
                nutritionProfile.Sex,
                nutritionProfile.ActivityLevel,
                goalType);
            var logsFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
            var logs = await dbContext.DailyNutritionSummaries
                .AsNoTracking()
                .Where(summary => summary.UserId == userId && summary.Date >= logsFrom)
                .ToArrayAsync(cancellationToken);
            var averageAdherence = logs.Length == 0
                ? (double?)null
                : logs.Average(summary => summary.Calories / nutritionTarget.CalorieTarget * 100);
            var weights = await dbContext.HealthMetrics
                .AsNoTracking()
                .Where(metric => metric.UserId == userId
                    && metric.Metric == "body_mass"
                    && metric.ObservedAt >= DateTimeOffset.UtcNow.AddDays(-14))
                .OrderByDescending(metric => metric.ObservedAt)
                .Take(60)
                .Select(metric => new AIHealthMetricContext(
                    metric.Metric,
                    metric.Value,
                    metric.Unit,
                    metric.ObservedAt))
                .ToArrayAsync(cancellationToken);
            nutritionContext = new AIUserNutritionContext(
                activeGoal?.Type.ToString() ?? goalType.ToString(),
                activeGoal?.TargetWeightKilograms,
                nutritionTarget.Bmr,
                nutritionTarget.Tdee,
                nutritionTarget.CalorieTarget,
                weight,
                new WeightTrendAnalyzer().Analyze(weights, DateTimeOffset.UtcNow),
                averageAdherence);
        }

        return new AIUserContext(
            profile?.DisplayName,
            profile?.Age,
            profile?.HeightCentimeters,
            profile?.Goal,
            metrics.OrderByDescending(metric => metric.ObservedAt).ToArray(),
            nutritionContext);
    }
}
