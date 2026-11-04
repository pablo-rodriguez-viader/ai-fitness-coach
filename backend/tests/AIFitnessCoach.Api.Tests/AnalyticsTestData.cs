using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Api.Tests;

/// <summary>Builders for analytics tests. Day 0 is 2026-10-01 (a Thursday), noon UTC.</summary>
internal static class AnalyticsTestData
{
    public static readonly DateOnly Day0 = new(2026, 10, 1);

    public static DateOnly Day(int offset) => Day0.AddDays(offset);

    public static DateTimeOffset At(int dayOffset, int hour = 12) =>
        new(Day(dayOffset).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    public static AnalyticsEvent Event(
        string userId,
        string name,
        int dayOffset,
        IReadOnlyDictionary<string, string>? properties = null,
        int hour = 12) =>
        new(userId, name, At(dayOffset, hour), properties ?? new Dictionary<string, string>());

    public static IEnumerable<AnalyticsEvent> Sessions(string userId, params int[] dayOffsets) =>
        dayOffsets.Select(day => Event(userId, ProductEventNames.SessionStart, day));

    public static WorkoutDayFact Workout(int plannedOffset, int? completedOffset = null, string title = "Fuerza") =>
        new(Guid.NewGuid(), Day(plannedOffset), title, completedOffset is { } done ? At(done) : null);

    public static GoalFact Goal(int startOffset, string status = "Active", int? updatedOffset = null) =>
        new(Guid.NewGuid(), "GeneralHealth", status, Day(startOffset), At(startOffset, 8), At(updatedOffset ?? startOffset, 8));

    public static UserActivitySnapshot Snapshot(
        string userId = "user-1",
        IEnumerable<AnalyticsEvent>? events = null,
        IEnumerable<WorkoutDayFact>? workouts = null,
        IEnumerable<int>? mealDays = null,
        IEnumerable<GoalFact>? goals = null,
        IReadOnlyDictionary<int, double>? steps = null) =>
        new(
            userId,
            (events ?? []).ToArray(),
            (workouts ?? []).ToArray(),
            (mealDays ?? []).Select(Day).ToArray(),
            (goals ?? []).ToArray(),
            (steps ?? new Dictionary<int, double>()).ToDictionary(pair => Day(pair.Key), pair => pair.Value));
}
