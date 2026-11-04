using System.Text.Json.Serialization;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Analytics;

[JsonConverter(typeof(JsonStringEnumConverter<DropoutRiskLevel>))]
public enum DropoutRiskLevel
{
    Low,
    Medium,
    High
}

/// <param name="Code">less_usage, less_activity, less_interaction or inactive.</param>
public sealed record DropoutSignal(string Code, string Description, double Previous, double Current);

/// <param name="HasEnoughHistory">False during the user's first week: there is nothing to compare yet.</param>
public sealed record DropoutRisk(
    string UserId,
    DropoutRiskLevel Level,
    int Score,
    bool HasEnoughHistory,
    int? DaysSinceLastActive,
    IReadOnlyList<DropoutSignal> Signals);

/// <summary>
/// Compares the last 7 days with the 7 before them. Signals: fewer active days (usage), fewer
/// steps or completed workouts (activity), fewer deliberate interactions (Coach, feedback,
/// meals, goals) and days without opening the app. Thresholds favour few false alarms.
/// </summary>
public sealed class DropoutRiskAnalyzer
{
    public const int HighScore = 60;
    public const int MediumScore = 30;

    public DropoutRisk Analyze(UserActivitySnapshot snapshot, DateOnly asOf)
    {
        var offset = TimeSpan.Zero;
        var activeDays = ActivityDays.Active(snapshot, offset).Where(day => day <= asOf).ToHashSet();
        if (activeDays.Count == 0)
            return new DropoutRisk(snapshot.UserId, DropoutRiskLevel.Low, 0, false, null, []);

        var firstDay = activeDays.Min();
        var lastActive = activeDays.Max();
        var daysSinceLastActive = asOf.DayNumber - lastActive.DayNumber;
        var hasHistory = asOf.DayNumber - firstDay.DayNumber >= 13;

        var currentStart = asOf.AddDays(-6);
        var previousStart = asOf.AddDays(-13);
        var previousEnd = asOf.AddDays(-7);
        bool InCurrent(DateOnly day) => day >= currentStart && day <= asOf;
        bool InPrevious(DateOnly day) => day >= previousStart && day <= previousEnd;

        var signals = new List<DropoutSignal>();
        var score = 0;

        if (daysSinceLastActive >= 3)
        {
            score += daysSinceLastActive >= 7 ? 45 : 20;
            signals.Add(new DropoutSignal(
                "inactive",
                $"Sin actividad desde hace {daysSinceLastActive} días.",
                0,
                daysSinceLastActive));
        }

        if (hasHistory)
        {
            var previousUsage = activeDays.Count(InPrevious);
            var currentUsage = activeDays.Count(InCurrent);
            if (previousUsage >= 2 && currentUsage <= previousUsage * 0.5)
            {
                score += 30;
                signals.Add(new DropoutSignal("less_usage", "Abre la app menos días que la semana anterior.", previousUsage, currentUsage));
            }

            var completed = snapshot.WorkoutDays
                .Where(day => day.CompletedAt is not null)
                .Select(day => AnalyticsCalendar.Day(day.CompletedAt!.Value, offset))
                .ToArray();
            var previousWorkouts = completed.Count(InPrevious);
            var currentWorkouts = completed.Count(InCurrent);
            var previousSteps = AverageSteps(snapshot, previousStart, previousEnd);
            var currentSteps = AverageSteps(snapshot, currentStart, asOf);
            var fewerWorkouts = previousWorkouts >= 2 && currentWorkouts <= previousWorkouts * 0.5;
            var fewerSteps = previousSteps is > 0 && (currentSteps ?? 0) <= previousSteps * 0.7;
            if (fewerWorkouts || fewerSteps)
            {
                score += 25;
                signals.Add(fewerWorkouts
                    ? new DropoutSignal("less_activity", "Completa menos entrenamientos.", previousWorkouts, currentWorkouts)
                    : new DropoutSignal("less_activity", "Su media de pasos ha bajado más de un 30 %.", previousSteps!.Value, currentSteps ?? 0));
            }

            var interactions = snapshot.Events
                .Where(item => ProductEventNames.Interactions.Contains(item.Name))
                .Select(item => AnalyticsCalendar.Day(item.OccurredAt, offset))
                .ToArray();
            var previousInteractions = interactions.Count(InPrevious);
            var currentInteractions = interactions.Count(InCurrent);
            if (previousInteractions >= 3 && currentInteractions <= previousInteractions * 0.5)
            {
                score += 25;
                signals.Add(new DropoutSignal(
                    "less_interaction",
                    "Interactúa menos con el Coach, el feedback o los registros.",
                    previousInteractions,
                    currentInteractions));
            }
        }

        score = Math.Min(score, 100);
        var level = score >= HighScore
            ? DropoutRiskLevel.High
            : score >= MediumScore ? DropoutRiskLevel.Medium : DropoutRiskLevel.Low;
        return new DropoutRisk(snapshot.UserId, level, score, hasHistory, daysSinceLastActive, signals);
    }

    private static double? AverageSteps(UserActivitySnapshot snapshot, DateOnly from, DateOnly to)
    {
        var values = snapshot.DailySteps
            .Where(pair => pair.Key >= from && pair.Key <= to)
            .Select(pair => pair.Value)
            .ToArray();
        // Missing days are unknown (no sync), not zero steps.
        return values.Length == 0 ? null : values.Average();
    }
}
