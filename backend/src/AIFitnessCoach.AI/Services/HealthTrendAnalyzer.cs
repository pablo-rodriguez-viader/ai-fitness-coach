using AIFitnessCoach.AI.Contracts;

namespace AIFitnessCoach.AI.Services;

public sealed class HealthTrendAnalyzer
{
    public HealthTrendSummary Analyze(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset now)
    {
        var weekStart = now.AddDays(-7);
        var previousWeekStart = now.AddDays(-14);
        var recentWeights = metrics
            .Where(metric => metric.Metric == "body_mass" && metric.ObservedAt >= now.AddDays(-30))
            .OrderBy(metric => metric.ObservedAt)
            .ToArray();
        var weightChange = recentWeights.Length >= 2
            ? recentWeights[^1].Value - recentWeights[0].Value
            : (double?)null;
        var averageWeight = recentWeights.Length >= 2
            ? recentWeights[0].Value
            : 0;

        var recentSteps = metrics
            .Where(metric => metric.Metric == "steps" && metric.ObservedAt >= weekStart)
            .ToArray();
        var previousSteps = metrics
            .Where(metric => metric.Metric == "steps"
                && metric.ObservedAt >= previousWeekStart
                && metric.ObservedAt < weekStart)
            .ToArray();
        var recentDailySteps = DailyAverage(recentSteps, 7);
        var previousDailySteps = DailyAverage(previousSteps, 7);
        var recentSleep = metrics
            .Where(metric => metric.Metric == "sleep_duration" && metric.ObservedAt >= weekStart)
            .ToArray();
        var previousSleep = metrics
            .Where(metric => metric.Metric == "sleep_duration"
                && metric.ObservedAt >= previousWeekStart
                && metric.ObservedAt < weekStart)
            .ToArray();
        var recentWorkouts = metrics.Count(metric =>
            metric.Metric == "workout_duration" && metric.ObservedAt >= weekStart);
        var previousWorkouts = metrics.Count(metric =>
            metric.Metric == "workout_duration"
            && metric.ObservedAt >= previousWeekStart
            && metric.ObservedAt < weekStart);

        return new HealthTrendSummary(
            weightChange,
            weightChange is null || averageWeight == 0
                ? null
                : weightChange.Value / averageWeight * 100,
            PercentChange(previousDailySteps, recentDailySteps),
            recentSleep.Length == 0 || previousSleep.Length == 0
                ? null
                : recentSleep.Average(metric => metric.Value) / 3600d
                    - previousSleep.Average(metric => metric.Value) / 3600d,
            recentWorkouts - previousWorkouts);
    }

    private static double DailyAverage(IReadOnlyList<AIHealthMetricContext> metrics, int days) =>
        metrics.Count == 0 ? 0 : metrics.Sum(metric => metric.Value) / days;

    private static double? PercentChange(double previous, double current) =>
        previous <= 0 ? null : (current - previous) / previous * 100;
}
