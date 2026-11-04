using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;
using Xunit;
using static AIFitnessCoach.Api.Tests.AnalyticsTestData;

namespace AIFitnessCoach.Api.Tests;

/// <summary>Reminder content the app turns into local notifications.</summary>
public sealed class NotificationTests
{
    private readonly MotivationEngine engine = new();

    [Fact]
    public void PendingWorkoutTodayProducesAWorkoutReminder()
    {
        var snapshot = Snapshot(workouts: [Workout(3, title: "Piernas suaves")]);

        var reminders = engine.Build(snapshot, At(3, 9), TimeSpan.Zero).Reminders;

        var reminder = Assert.Single(reminders, item => item.Kind == ReminderKinds.PendingWorkout);
        Assert.Equal("Hoy toca: Piernas suaves.", reminder.Body);
        Assert.Equal("aifitnesscoach://workouts", reminder.DeepLink);
        Assert.Equal(Day(3), reminder.Date);
    }

    [Fact]
    public void CompletedWorkoutDoesNotProduceAReminder()
    {
        var snapshot = Snapshot(workouts: [Workout(3, completedOffset: 3)]);

        var reminders = engine.Build(snapshot, At(3, 19), TimeSpan.Zero).Reminders;

        Assert.DoesNotContain(reminders, item => item.Kind == ReminderKinds.PendingWorkout);
    }

    [Fact]
    public void DailyGoalReminderOnlyWhenAGoalIsActiveAndNothingWasDoneToday()
    {
        var withGoal = Snapshot(goals: [Goal(0)]);
        var alreadyDone = Snapshot(goals: [Goal(0)], mealDays: [3]);
        var withoutGoal = Snapshot();

        Assert.Contains(engine.Build(withGoal, At(3), TimeSpan.Zero).Reminders, item => item.Kind == ReminderKinds.DailyGoal);
        Assert.DoesNotContain(engine.Build(alreadyDone, At(3), TimeSpan.Zero).Reminders, item => item.Kind == ReminderKinds.DailyGoal);
        Assert.DoesNotContain(engine.Build(withoutGoal, At(3), TimeSpan.Zero).Reminders, item => item.Kind == ReminderKinds.DailyGoal);
    }

    [Fact]
    public void DailyGoalReminderMentionsAStreakAtRisk()
    {
        var snapshot = Snapshot(goals: [Goal(0)], mealDays: [1, 2]);

        var reminder = engine.Build(snapshot, At(3), TimeSpan.Zero).Reminders
            .Single(item => item.Kind == ReminderKinds.DailyGoal);

        Assert.Contains("racha de 2 días", reminder.Body);
    }

    [Fact]
    public void WeeklySummaryIsScheduledForTheNextSunday()
    {
        // Day 0 is Thursday 2026-10-01, so the next Sunday is day 3; on Sunday it is the same day.
        var fromThursday = engine.Build(Snapshot(), At(0), TimeSpan.Zero).Reminders
            .Single(item => item.Kind == ReminderKinds.WeeklySummary);
        var fromSunday = engine.Build(Snapshot(), At(3), TimeSpan.Zero).Reminders
            .Single(item => item.Kind == ReminderKinds.WeeklySummary);

        Assert.Equal(DayOfWeek.Sunday, fromThursday.Date.DayOfWeek);
        Assert.Equal(Day(3), fromThursday.Date);
        Assert.Equal(Day(3), fromSunday.Date);
        Assert.Equal("aifitnesscoach://summary", fromThursday.DeepLink);
    }

    [Fact]
    public void WeeklySummaryReminderReflectsTheWeek()
    {
        var snapshot = Snapshot(
            workouts: [Workout(1, 1), Workout(2, 2)],
            events: Sessions("user-1", 1, 2, 3));

        var reminder = engine.Build(snapshot, At(3), TimeSpan.Zero).Reminders
            .Single(item => item.Kind == ReminderKinds.WeeklySummary);

        Assert.Contains("3 días activos", reminder.Body);
        Assert.Contains("2 entrenamientos", reminder.Body);
    }

    [Fact]
    public void RemindersUseTheUsersLocalDay()
    {
        // 22:30 UTC on day 2 is already day 3 in UTC+2, when the workout is planned.
        var snapshot = Snapshot(workouts: [Workout(3)]);
        var now = At(2, 22).AddMinutes(30);

        var utc = engine.Build(snapshot, now, TimeSpan.Zero).Reminders;
        var local = engine.Build(snapshot, now, TimeSpan.FromHours(2)).Reminders;

        Assert.DoesNotContain(utc, item => item.Kind == ReminderKinds.PendingWorkout);
        Assert.Contains(local, item => item.Kind == ReminderKinds.PendingWorkout);
    }

    [Fact]
    public void OpeningTheAppDoesNotCountAsGoalProgress()
    {
        var snapshot = Snapshot(goals: [Goal(0)], events: [Event("user-1", ProductEventNames.SessionStart, 3)]);

        var reminders = engine.Build(snapshot, At(3, 18), TimeSpan.Zero).Reminders;

        Assert.Contains(reminders, item => item.Kind == ReminderKinds.DailyGoal);
    }
}
