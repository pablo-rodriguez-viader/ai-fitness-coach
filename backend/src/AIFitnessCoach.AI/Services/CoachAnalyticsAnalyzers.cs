using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class TrendAnalyzer
{
    private static readonly (string Metric, string Label, string Unit, bool DailySum)[] SupportedMetrics =
    [
        ("body_mass", "weight", "kg", false),
        ("sleep_duration", "sleep", "hours", true),
        ("steps", "steps", "steps/day", true),
        ("heart_rate", "heart_rate", "bpm", false),
        ("resting_heart_rate", "resting_heart_rate", "bpm", false),
        ("active_energy", "active_calories", "kcal/day", true),
        ("active_calories", "active_calories", "kcal/day", true),
        ("total_energy", "total_calories", "kcal/day", true),
        ("total_calories", "total_calories", "kcal/day", true)
    ];

    public IReadOnlyList<MetricTrendView> Analyze(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset now)
    {
        var result = new List<MetricTrendView>();
        foreach (var definition in SupportedMetrics.GroupBy(item => item.Label).Select(group => group.First()))
        {
            var current = definition.Label == "total_calories"
                ? AverageTotalCaloriesByDay(metrics, now.AddDays(-7), now)
                : AverageByDay(metrics, definition.Metric, now.AddDays(-7), now, definition.DailySum);
            var previous = definition.Label == "total_calories"
                ? AverageTotalCaloriesByDay(metrics, now.AddDays(-14), now.AddDays(-7))
                : AverageByDay(metrics, definition.Metric, now.AddDays(-14), now.AddDays(-7), definition.DailySum);
            double? change = current is null || previous is null
                ? null
                : Math.Round(current.Value - previous.Value, 2, MidpointRounding.AwayFromZero);
            result.Add(new MetricTrendView(definition.Label, change, definition.Unit));
        }
        return result;
    }

    private static double? AverageTotalCaloriesByDay(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var daily = metrics
            .Where(item => item.Metric is "total_energy" or "active_energy" or "basal_energy"
                && item.ObservedAt >= from && item.ObservedAt < to)
            .GroupBy(item => item.ObservedAt.UtcDateTime.Date)
            .Select(day =>
            {
                var total = day.Where(item => item.Metric == "total_energy").Sum(item => item.Value);
                if (total > 0)
                    return (double?)total;
                var active = day.Where(item => item.Metric == "active_energy").ToArray();
                var basal = day.Where(item => item.Metric == "basal_energy").ToArray();
                return active.Length > 0 && basal.Length > 0
                    ? active.Sum(item => item.Value) + basal.Sum(item => item.Value)
                    : null;
            })
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return daily.Length == 0 ? null : daily.Average();
    }

    private static double? AverageByDay(
        IReadOnlyList<AIHealthMetricContext> metrics,
        string metric,
        DateTimeOffset from,
        DateTimeOffset to,
        bool sumByDay)
    {
        var matching = metrics.Where(item =>
            string.Equals(item.Metric, metric, StringComparison.OrdinalIgnoreCase)
            && item.ObservedAt >= from && item.ObservedAt < to);
        var groups = matching.GroupBy(item => item.ObservedAt.UtcDateTime.Date)
            .Select(day => sumByDay ? day.Sum(item => item.Value) : day.Average(item => item.Value))
            .ToArray();
        if (groups.Length == 0)
            return null;
        var average = groups.Average();
        return string.Equals(metric, "sleep_duration", StringComparison.OrdinalIgnoreCase)
            ? average / 3600d
            : average;
    }
}

public sealed class AdherenceAnalyzer
{
    public TrainingAdherence Analyze(
        string userId,
        IReadOnlyList<WorkoutPlan> plans,
        IReadOnlyList<WorkoutHistory> history,
        DateTimeOffset now)
    {
        var end = DateOnly.FromDateTime(now.UtcDateTime);
        var start = end.AddDays(-27);
        var plannedDays = plans.SelectMany(plan => plan.Days)
            .Where(day => day.Date >= start && day.Date <= end)
            .ToArray();
        var completed = history
            .Where(item => item.Completed && item.Date >= start && item.Date <= end)
            .Select(item => item.WorkoutDayId)
            .ToHashSet();
        var plannedIds = plannedDays.Select(day => day.Id).ToHashSet();
        var completedCount = plannedIds.Count(completed.Contains);
        return TrainingAdherence.Create(
            userId,
            start,
            end,
            plannedIds.Count,
            completedCount,
            now);
    }
}

public sealed class BehaviorAnalyzer
{
    public IReadOnlyList<BehaviorPattern> Analyze(
        string userId,
        TrainingAdherence adherence,
        IReadOnlyList<MetricTrendView> trends,
        IReadOnlyList<WorkoutFeedback> feedback,
        DateTimeOffset now)
    {
        var patterns = new List<BehaviorPattern>();
        if (adherence.PlannedSessions > 0)
        {
            var consistency = adherence.Rate >= 0.75;
            patterns.Add(BehaviorPattern.Create(
                userId,
                "training_adherence",
                consistency
                    ? "La mayoría de las sesiones planificadas se completaron."
                    : "La adherencia mejora al ajustar el plan a una carga más sostenible.",
                Math.Min(1, adherence.PlannedSessions / 8d),
                adherence.PlannedSessions,
                now));
        }

        var sleep = trends.FirstOrDefault(item => item.Metric == "sleep");
        var completionRate = feedback.Count == 0 ? (double?)null : feedback.Count(item => item.Completed) / (double)feedback.Count;
        if (sleep?.Change is < -0.5 && completionRate is < 0.6)
        {
            patterns.Add(BehaviorPattern.Create(
                userId,
                "recovery_and_adherence",
                "Las semanas con menos sueño reciente coinciden con menor finalización de entrenamientos.",
                Math.Min(1, feedback.Count / 10d),
                feedback.Count,
                now));
        }

        if (feedback.Count >= 3)
        {
            var easierSessionsSucceeded = feedback.Count(item =>
                item.Completed && item.Difficulty is not (WorkoutDifficulty.Hard or WorkoutDifficulty.VeryHard));
            patterns.Add(BehaviorPattern.Create(
                userId,
                "workout_difficulty_fit",
                easierSessionsSucceeded >= feedback.Count / 2
                    ? "Las sesiones de dificultad manejable suelen completarse."
                    : "Conviene revisar duración e intensidad de las sesiones para mejorar su finalización.",
                Math.Min(1, feedback.Count / 10d),
                feedback.Count,
                now));
        }
        return patterns;
    }
}

public sealed class GoalAchievementAnalyzer
{
    public IReadOnlyList<GoalAchievementView> Analyze(
        IReadOnlyList<Goal> goals,
        IReadOnlyList<AIHealthMetricContext> metrics)
    {
        var latestWeight = metrics.Where(metric => metric.Metric == "body_mass")
            .OrderByDescending(metric => metric.ObservedAt)
            .Select(metric => (double?)metric.Value)
            .FirstOrDefault();
        return goals.Select(goal =>
        {
            double? progress = goal.Status == GoalStatus.Completed
                ? 100
                : goal.TargetWeightKilograms is { } target && latestWeight is { } current
                    ? EstimateWeightProgress(metrics, target, current)
                    : null;
            return new GoalAchievementView(goal.Id, goal.Type.ToString(), goal.Status.ToString(), progress);
        }).ToArray();
    }

    private static double? EstimateWeightProgress(
        IReadOnlyList<AIHealthMetricContext> metrics,
        double target,
        double current)
    {
        var baseline = metrics.Where(metric => metric.Metric == "body_mass")
            .OrderBy(metric => metric.ObservedAt)
            .Select(metric => (double?)metric.Value)
            .FirstOrDefault();
        if (baseline is null || Math.Abs(baseline.Value - target) < 0.01)
            return null;
        return Math.Clamp(
            (Math.Abs(baseline.Value - target) - Math.Abs(current - target))
                / Math.Abs(baseline.Value - target) * 100,
            0,
            100);
    }
}
