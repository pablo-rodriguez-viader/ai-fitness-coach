using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;
using Xunit;
using static AIFitnessCoach.Api.Tests.AnalyticsTestData;

namespace AIFitnessCoach.Api.Tests;

public sealed class AdherenceTests
{
    private readonly AdherenceEngine engine = new();

    [Fact]
    public void TrainingAdherenceIsCompletedPlannedDaysInThePeriod()
    {
        var snapshot = Snapshot(workouts:
        [
            Workout(0, completedOffset: 0),
            Workout(2, completedOffset: 3),   // completed late still counts
            Workout(4),
            Workout(6),
            Workout(20, completedOffset: 20)  // outside the period
        ]);

        var report = engine.Calculate(snapshot, Day(0), Day(6), TimeSpan.Zero);

        Assert.Equal(new AdherenceRate(2, 4, 0.5), report.Training);
    }

    [Fact]
    public void TrainingAdherenceIsUnknownWithoutAPlan()
    {
        var report = engine.Calculate(Snapshot(), Day(0), Day(6), TimeSpan.Zero);

        Assert.Null(report.Training.Rate);
        Assert.Equal(0, report.Training.Expected);
    }

    [Fact]
    public void NutritionAdherenceStartsAtTheFirstLoggedMeal()
    {
        var snapshot = Snapshot(mealDays: [3, 3, 4, 6]);

        var report = engine.Calculate(snapshot, Day(0), Day(6), TimeSpan.Zero);

        // Days 3–6 are expected (4), meals on 3 distinct days.
        Assert.Equal(new AdherenceRate(3, 4, 0.75), report.Nutrition);
    }

    [Fact]
    public void NutritionAdherenceIsUnknownForUsersWhoNeverLogMeals()
    {
        var report = engine.Calculate(Snapshot(), Day(0), Day(6), TimeSpan.Zero);

        Assert.Null(report.Nutrition.Rate);
    }

    [Fact]
    public void GoalAdherenceCountsDaysWithProgressWhileAGoalIsActive()
    {
        var snapshot = Snapshot(
            goals: [Goal(2)],
            workouts: [Workout(2, completedOffset: 2)],
            mealDays: [4],
            events: [Event("user-1", ProductEventNames.CoachQuery, 5), Event("user-1", ProductEventNames.SessionStart, 6)]);

        var report = engine.Calculate(snapshot, Day(0), Day(6), TimeSpan.Zero);

        // Active goal on days 2–6 (5 days); progress on days 2, 4, 5. Opening the app is not progress.
        Assert.Equal(new AdherenceRate(3, 5, 0.6), report.Goals);
    }

    [Fact]
    public void PausedGoalsStopCountingAfterTheyChangeStatus()
    {
        var snapshot = Snapshot(goals: [Goal(0, status: "Paused", updatedOffset: 2)], mealDays: [1]);

        var report = engine.Calculate(snapshot, Day(0), Day(6), TimeSpan.Zero);

        Assert.Equal(3, report.Goals.Expected);
        Assert.Equal(1, report.Goals.Achieved);
    }

    [Fact]
    public void UtcOffsetMovesLateNightActivityToTheUsersDay()
    {
        // 23:30 UTC on day 1 is 01:30 on day 2 in Madrid summer time (UTC+2).
        var lateWorkout = new WorkoutDayFact(Guid.NewGuid(), Day(2), "Cardio", At(1, 23).AddMinutes(30));
        var snapshot = Snapshot(goals: [Goal(0)], workouts: [lateWorkout]);

        var utc = engine.Calculate(snapshot, Day(2), Day(2), TimeSpan.Zero);
        var madrid = engine.Calculate(snapshot, Day(2), Day(2), TimeSpan.FromHours(2));

        Assert.Equal(0, utc.Goals.Achieved);
        Assert.Equal(1, madrid.Goals.Achieved);
    }

    [Fact]
    public void InvalidPeriodIsRejected()
    {
        Assert.Throws<ArgumentException>(() => engine.Calculate(Snapshot(), Day(5), Day(1), TimeSpan.Zero));
    }
}
