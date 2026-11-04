using AIFitnessCoach.AI.Contracts;

namespace AIFitnessCoach.AI.Services;

public sealed class HealthSummaryAnalyzer
{
    public WeeklyHealthSummary BuildWeekly(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset now,
        int daysOffset = 0)
    {
        var end = now.AddDays(-daysOffset);
        var start = end.AddDays(-7);
        return Build(metrics, start, end);
    }

    public MonthlyHealthSummary BuildMonthly(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset now)
    {
        var end = now;
        var start = now.AddDays(-30);
        var summary = Build(metrics, start, end);
        return new MonthlyHealthSummary(
            summary.PeriodStart,
            summary.PeriodEnd,
            summary.Steps,
            summary.AverageSleepHours,
            summary.WeightKilograms,
            summary.Workouts,
            summary.WorkoutMinutes,
            summary.AverageHeartRate,
            summary.DaysWithSteps,
            summary.DaysWithWorkouts);
    }

    private static WeeklyHealthSummary Build(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var period = metrics.Where(metric => metric.ObservedAt >= start && metric.ObservedAt < end).ToArray();
        var steps = period.Where(metric => metric.Metric == "steps").ToArray();
        var sleep = period.Where(metric => metric.Metric == "sleep_duration").ToArray();
        var weights = period.Where(metric => metric.Metric == "body_mass")
            .OrderBy(metric => metric.ObservedAt)
            .ToArray();
        var workouts = period.Where(metric => metric.Metric == "workout_duration").ToArray();
        var heartRates = period.Where(metric => metric.Metric is "heart_rate" or "resting_heart_rate").ToArray();

        return new WeeklyHealthSummary(
            start,
            end,
            steps.Length == 0 ? null : steps.Sum(metric => metric.Value),
            sleep.Length == 0 ? null : sleep.Average(metric => metric.Value) / 3600d,
            weights.LastOrDefault()?.Value,
            workouts.Length == 0 ? null : workouts.Length,
            workouts.Length == 0 ? null : workouts.Sum(metric => metric.Value) / 60d,
            heartRates.Length == 0 ? null : heartRates.Average(metric => metric.Value),
            steps.Select(metric => metric.ObservedAt.UtcDateTime.Date).Distinct().Count(),
            workouts.Select(metric => metric.ObservedAt.UtcDateTime.Date).Distinct().Count());
    }
}
