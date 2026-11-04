using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Analytics;

/// <param name="Rate">0–1; null when there was nothing to adhere to (no plan, no goal, feature unused).</param>
public sealed record AdherenceRate(int Achieved, int Expected, double? Rate)
{
    public static AdherenceRate From(int achieved, int expected) =>
        new(achieved, expected, expected == 0 ? null : Math.Round(achieved / (double)expected, 3));
}

public sealed record AdherenceReport(
    DateOnly From,
    DateOnly To,
    AdherenceRate Training,
    AdherenceRate Nutrition,
    AdherenceRate Goals);

/// <summary>
/// Training: planned workout days in the period that were completed.
/// Nutrition: days with at least one meal logged, counted from the first day the user logged
/// a meal (users who never log meals have no nutrition adherence, not 0%).
/// Goals: days with an active goal on which the user recorded progress (a workout, a meal,
/// feedback or a Coach question).
/// </summary>
public sealed class AdherenceEngine
{
    public AdherenceReport Calculate(UserActivitySnapshot snapshot, DateOnly from, DateOnly to, TimeSpan utcOffset)
    {
        if (to < from)
            throw new ArgumentException("The period end must not precede its start.", nameof(to));

        var plannedDays = snapshot.WorkoutDays
            .Where(day => day.PlannedOn >= from && day.PlannedOn <= to)
            .ToArray();
        var training = AdherenceRate.From(plannedDays.Count(day => day.CompletedAt is not null), plannedDays.Length);

        var nutrition = AdherenceRate.From(0, 0);
        if (snapshot.NutritionLogDates.Count > 0)
        {
            var firstLog = snapshot.NutritionLogDates.Min();
            var start = firstLog > from ? firstLog : from;
            if (start <= to)
            {
                var loggedDays = snapshot.NutritionLogDates.Where(day => day >= start && day <= to).Distinct().Count();
                nutrition = AdherenceRate.From(loggedDays, to.DayNumber - start.DayNumber + 1);
            }
        }

        var progressDays = ActivityDays.Progress(snapshot, utcOffset);
        var goalDays = AnalyticsCalendar.Range(from, to)
            .Where(day => snapshot.Goals.Any(goal => IsActiveOn(goal, day, utcOffset)))
            .ToArray();
        var goals = AdherenceRate.From(goalDays.Count(progressDays.Contains), goalDays.Length);

        return new AdherenceReport(from, to, training, nutrition, goals);
    }

    /// <summary>A goal counts from its start until it stops being active (paused, completed…).</summary>
    public static bool IsActiveOn(GoalFact goal, DateOnly day, TimeSpan utcOffset)
    {
        if (day < goal.StartsOn || day < AnalyticsCalendar.Day(goal.CreatedAt, utcOffset))
            return false;
        return goal.Status == "Active" || day <= AnalyticsCalendar.Day(goal.UpdatedAt, utcOffset);
    }
}

/// <summary>Which days count as "the user did something that moves them forward".</summary>
public static class ActivityDays
{
    public static HashSet<DateOnly> Progress(UserActivitySnapshot snapshot, TimeSpan utcOffset)
    {
        var days = snapshot.Events
            .Where(item => ProductEventNames.ProgressActions.Contains(item.Name))
            .Select(item => AnalyticsCalendar.Day(item.OccurredAt, utcOffset))
            .ToHashSet();
        foreach (var workout in snapshot.WorkoutDays)
        {
            if (workout.CompletedAt is { } completedAt)
                days.Add(AnalyticsCalendar.Day(completedAt, utcOffset));
        }
        days.UnionWith(snapshot.NutritionLogDates);
        return days;
    }

    /// <summary>Days with any product event (opening the app counts).</summary>
    public static HashSet<DateOnly> Active(UserActivitySnapshot snapshot, TimeSpan utcOffset)
    {
        var days = snapshot.Events
            .Select(item => AnalyticsCalendar.Day(item.OccurredAt, utcOffset))
            .ToHashSet();
        days.UnionWith(Progress(snapshot, utcOffset));
        return days;
    }
}
