using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class ProgressAnalyzer
{
    private readonly GoalAnalyzer goalAnalyzer = new();

    public string AnalyzeCoachProgress(
        IReadOnlyList<MetricTrendView> trends,
        IReadOnlyList<GoalAchievementView> goals)
    {
        var improvements = trends
            .Where(item => item.Change is > 0 && item.Metric is "steps" or "sleep")
            .Select(item => item.Metric)
            .ToArray();
        if (improvements.Length > 0)
            return $"Se observa una mejora reciente en {string.Join(" y ", improvements)}. Mantén los cambios graduales.";
        if (trends.All(item => item.Change is null))
            return "Todavía no hay datos suficientes en ambos periodos para comparar tendencias.";
        if (goals.Any(goal => goal.Status == "Completed"))
            return "Hay objetivos completados en tu historial. Revisa las tendencias recientes y define el siguiente paso con calma.";
        return "La evolución reciente es mixta o estable; revisa la constancia y ajusta una variable cada vez.";
    }

    public ProgressSummary Analyze(
        WeeklyHealthSummary currentWeek,
        WeeklyHealthSummary previousWeek,
        string? goal)
    {
        var activeDays = currentWeek.DaysWithSteps;
        var previousActiveDays = previousWeek.DaysWithSteps;
        var consistency = activeDays switch
        {
            >= 6 => "high",
            >= 4 => "good",
            >= 2 => "building",
            _ => "low"
        };
        var normalizedGoal = goal?.ToLowerInvariant() ?? string.Empty;
        var aligned = normalizedGoal.Contains("run", StringComparison.Ordinal)
            || normalizedGoal.Contains("cardio", StringComparison.Ordinal)
            || normalizedGoal.Contains("endurance", StringComparison.Ordinal)
            ? "cardiovascular activity and recorded workouts are most relevant to this goal"
            : string.IsNullOrWhiteSpace(normalizedGoal)
                ? null
                : "use the recorded activity trend to check progress toward the stated goal";
        var suggestion = currentWeek.AverageSleepHours is < 7
            ? "consider making sleep and recovery more consistent"
            : activeDays < 4
                ? "look for a realistic way to add one active day next week"
                : "maintain the current routine and adjust gradually based on how you feel";

        return new ProgressSummary(
            consistency,
            aligned,
            suggestion,
            activeDays,
            previousActiveDays);
    }

    public ProgressView Analyze(
        Goal? goal,
        IReadOnlyList<AIHealthMetricContext> healthMetrics,
        IReadOnlyList<DailyNutritionSummary> nutritionSummaries,
        IReadOnlyList<GoalProgress> savedProgress)
    {
        var weights = healthMetrics.Where(metric => metric.Metric == "body_mass")
            .GroupBy(metric => DateOnly.FromDateTime(metric.ObservedAt.UtcDateTime))
            .ToDictionary(group => group.Key, group => (double?)group.OrderByDescending(metric => metric.ObservedAt).First().Value);
        var steps = healthMetrics.Where(metric => metric.Metric == "steps")
            .GroupBy(metric => DateOnly.FromDateTime(metric.ObservedAt.UtcDateTime))
            .ToDictionary(group => group.Key, group => (double?)group.Sum(metric => metric.Value));
        var sleep = healthMetrics.Where(metric => metric.Metric == "sleep_duration")
            .GroupBy(metric => DateOnly.FromDateTime(metric.ObservedAt.UtcDateTime))
            .ToDictionary(group => group.Key, group => (double?)(group.Sum(metric => metric.Value) / 3600d));
        var calories = nutritionSummaries.ToDictionary(summary => summary.Date, summary => (double?)summary.Calories);
        var dates = weights.Keys.Concat(steps.Keys).Concat(sleep.Keys).Concat(calories.Keys)
            .Distinct().OrderBy(date => date).ToArray();
        var history = dates.Select(date => new ProgressPoint(
            date,
            weights.GetValueOrDefault(date),
            calories.GetValueOrDefault(date),
            steps.GetValueOrDefault(date),
            sleep.GetValueOrDefault(date))).ToArray();
        var currentWeight = weights.OrderBy(pair => pair.Key).LastOrDefault().Value;
        var priorRecordedWeight = savedProgress.OrderBy(progress => progress.Date)
            .Select(progress => progress.WeightKilograms)
            .FirstOrDefault(weight => weight is not null);
        var previousWeight = priorRecordedWeight
            ?? weights.OrderBy(pair => pair.Key).Skip(Math.Max(0, weights.Count - 2)).FirstOrDefault().Value;
        var viewGoal = goal is null
            ? null
            : new GoalView(
                goal.Id,
                goal.Type.ToString(),
                goal.Status.ToString(),
                goal.TargetWeightKilograms,
                goal.StartsOn,
                goal.TargetDate,
                goal.CreatedAt);
        var weightRate = new WeightTrendAnalyzer().AnalyzeWeeklyRate(healthMetrics, DateTimeOffset.UtcNow);
        var safetyWarnings = new List<string>();
        if (currentWeight is { } measuredWeight && weightRate is { } rate)
        {
            var maximumWeeklyChange = measuredWeight * 0.01;
            if (rate < -maximumWeeklyChange)
                safetyWarnings.Add("La pérdida de peso reciente supera aproximadamente el 1 % del peso por semana.");
            else if (rate > maximumWeeklyChange)
                safetyWarnings.Add("La ganancia de peso reciente supera aproximadamente el 1 % del peso por semana.");
        }
        return new ProgressView(
            viewGoal,
            currentWeight,
            goal?.TargetWeightKilograms,
            currentWeight is { } latest && previousWeight is { } prior ? latest - prior : null,
            goal is null ? null : goalAnalyzer.CompletionPercent(goal, currentWeight),
            history,
            weightRate,
            safetyWarnings);
    }
}
