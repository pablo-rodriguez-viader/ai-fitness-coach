using System.Text.Json;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class AIContextBuilder(
    IAIContextSource source,
    HealthSummaryAnalyzer summaryAnalyzer,
    HealthTrendAnalyzer trendAnalyzer,
    ProgressAnalyzer progressAnalyzer,
    TimeProvider timeProvider,
    ICoachInsightsRepository? insightsRepository = null,
    IWellnessRepository? wellnessRepository = null,
    IWorkoutRepository? workoutRepository = null,
    TrendAnalyzer? coachTrendAnalyzer = null)
{
    public const int CurrentContextVersion = 3;

    public async Task<AIContextDocument> BuildAsync(
        string userId,
        int lookbackDays,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var context = await source.GetContextAsync(
            userId,
            now.AddDays(-lookbackDays),
            cancellationToken);

        // Data minimisation: the name never goes to the AI provider (the consent text says so).
        var userContext = context.Age is null && context.HeightCentimeters is null
            ? null
            : JsonSerializer.Serialize(new
            {
                context.Age,
                context.HeightCentimeters
            });
        var goalContext = string.IsNullOrWhiteSpace(context.Goal)
            ? null
            : JsonSerializer.Serialize(new
            {
                objective = context.Goal,
                activeGoal = context.Nutrition?.ActiveGoal,
                targetWeightKilograms = context.Nutrition?.TargetWeightKilograms
            });
        var weekly = summaryAnalyzer.BuildWeekly(context.RecentMetrics, now);
        var previousWeek = summaryAnalyzer.BuildWeekly(context.RecentMetrics, now, daysOffset: 7);
        var monthly = summaryAnalyzer.BuildMonthly(context.RecentMetrics, now);
        var trendMetrics = insightsRepository is null
            ? context.RecentMetrics
            : await insightsRepository.GetTrendMetricsAsync(userId, now.AddDays(-90), cancellationToken);
        var trends = trendAnalyzer.Analyze(trendMetrics, now);
        var coachTrends = (coachTrendAnalyzer ?? new TrendAnalyzer()).Analyze(trendMetrics, now);
        var progress = progressAnalyzer.Analyze(weekly, previousWeek, context.Goal);
        var healthContext = BuildHealthContext(context.RecentMetrics, now);
        var coachMemory = await BuildCoachMemoryAsync(userId, cancellationToken);
        return new AIContextDocument(
            userContext,
            goalContext,
            healthContext,
            CurrentContextVersion,
            weekly,
            monthly,
            trends,
            progress,
            NutritionContext: context.Nutrition,
            CoachMemoryContext: coachMemory,
            CoachTrends: coachTrends);
    }

    private async Task<string?> BuildCoachMemoryAsync(string userId, CancellationToken cancellationToken)
    {
        if (insightsRepository is null)
            return null;
        var patterns = await insightsRepository.GetPatternsAsync(userId, cancellationToken);
        var recommendations = await insightsRepository.GetRecommendationsAsync(userId, 10, cancellationToken);
        var outcomes = await insightsRepository.GetOutcomesAsync(userId, 100, cancellationToken);
        IReadOnlyList<Goal> goals = wellnessRepository is null
            ? Array.Empty<Goal>()
            : await wellnessRepository.GetGoalsAsync(userId, cancellationToken);
        IReadOnlyList<WorkoutPlan> plans = workoutRepository is null
            ? Array.Empty<WorkoutPlan>()
            : await workoutRepository.GetPlansAsync(userId, 5, cancellationToken);
        IReadOnlyList<WorkoutFeedback> feedback = workoutRepository is null
            ? Array.Empty<WorkoutFeedback>()
            : await workoutRepository.GetRecentFeedbackAsync(userId, 10, cancellationToken);
        IReadOnlyList<WorkoutHistory> workoutHistory = workoutRepository is null
            ? Array.Empty<WorkoutHistory>()
            : await workoutRepository.GetHistoryAsync(userId, 30, cancellationToken);
        var latestOutcomes = outcomes.GroupBy(item => item.RecommendationHistoryId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CreatedAt).First());
        var memory = new
        {
            previousGoals = goals.Take(20).Select(goal => new { goal.Type, goal.Status, goal.CreatedAt }),
            previousRoutines = plans.Select(plan => new
            {
                plan.Goal,
                plan.CreatedAt,
                days = plan.Days.OrderBy(day => day.Date).Select(day => new
                {
                    day.Date,
                    day.Title,
                    exercises = day.Exercises.OrderBy(exercise => exercise.Position)
                        .Take(10)
                        .Select(exercise => exercise.Name)
                })
            }),
            workoutHistory = workoutHistory.Select(item => new
            {
                item.Goal,
                item.Title,
                item.Date,
                item.Completed,
                item.Difficulty
            }),
            workoutFeedback = feedback.Select(item => new
            {
                item.Completed,
                difficulty = item.Difficulty.ToString(),
                comment = item.Comment is null ? null : item.Comment[..Math.Min(item.Comment.Length, 200)],
                item.CreatedAt
            }),
            behaviorPatterns = patterns.Select(pattern => new
            {
                pattern.Key,
                pattern.Description,
                pattern.Confidence,
                pattern.EvidenceCount
            }),
            recommendations = recommendations.Select(recommendation =>
            {
                latestOutcomes.TryGetValue(recommendation.Id, out var outcome);
                return new
                {
                    recommendation.Type,
                    text = recommendation.Text[..Math.Min(recommendation.Text.Length, 1000)],
                    recommendation.CreatedAt,
                    outcome?.Score,
                    outcome?.Feedback
                };
            })
        };
        return JsonSerializer.Serialize(memory);
    }

    private static string BuildHealthContext(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset now)
    {
        var contextMetrics = metrics
            .OrderByDescending(metric => metric.ObservedAt)
            .Take(100)
            .OrderBy(metric => metric.ObservedAt)
            .ToArray();
        var startOfWeek = now.AddDays(-7);
        var recent = contextMetrics.Where(metric => metric.ObservedAt >= startOfWeek).ToArray();
        var steps = recent.Where(metric => metric.Metric == "steps").Sum(metric => metric.Value);
        var sleep = recent.Where(metric => metric.Metric == "sleep_duration").ToArray();
        var heartRate = recent.Where(metric => metric.Metric == "heart_rate").ToArray();
        var workouts = recent.Where(metric => metric.Metric == "workout_duration").ToArray();
        var weights = contextMetrics.Where(metric => metric.Metric == "body_mass")
            .OrderBy(metric => metric.ObservedAt)
            .ToArray();

        var summary = new
        {
            periodStart = startOfWeek,
            periodEnd = now,
            stepsLast7Days = steps,
            averageSleepHoursLast7Days = sleep.Length == 0
                ? (double?)null
                : sleep.Average(metric => metric.Value) / 3600d,
            averageHeartRateLast7Days = heartRate.Length == 0
                ? (double?)null
                : heartRate.Average(metric => metric.Value),
            workoutsLast7Days = workouts.Length,
            workoutMinutesLast7Days = workouts.Sum(metric => metric.Value) / 60d,
            currentWeightKilograms = weights.LastOrDefault()?.Value,
            earliestWeightKilograms = weights.FirstOrDefault()?.Value,
            latestWeightKilograms = weights.LastOrDefault()?.Value,
            recentMetrics = contextMetrics
        };
        return JsonSerializer.Serialize(summary);
    }
}
