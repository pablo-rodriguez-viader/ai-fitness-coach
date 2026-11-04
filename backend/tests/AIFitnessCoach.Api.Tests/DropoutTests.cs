using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;
using Xunit;
using static AIFitnessCoach.Api.Tests.AnalyticsTestData;

namespace AIFitnessCoach.Api.Tests;

public sealed class DropoutTests
{
    private readonly DropoutRiskAnalyzer analyzer = new();

    // Report date: day 13. Previous week = days 0–6, current week = days 7–13.

    [Fact]
    public void SteadyUserIsLowRisk()
    {
        var snapshot = Snapshot(events: Sessions("user-1", Enumerable.Range(0, 14).ToArray()));

        var risk = analyzer.Analyze(snapshot, Day(13));

        Assert.Equal(DropoutRiskLevel.Low, risk.Level);
        Assert.Empty(risk.Signals);
        Assert.True(risk.HasEnoughHistory);
        Assert.Equal(0, risk.DaysSinceLastActive);
    }

    [Fact]
    public void FewerActiveDaysIsDetectedAsLessUsage()
    {
        var snapshot = Snapshot(events: Sessions("user-1", 0, 1, 2, 3, 4, 5, 12, 13));

        var risk = analyzer.Analyze(snapshot, Day(13));

        var signal = Assert.Single(risk.Signals);
        Assert.Equal("less_usage", signal.Code);
        Assert.Equal((6, 2), (signal.Previous, signal.Current));
        Assert.Equal(DropoutRiskLevel.Medium, risk.Level);
    }

    [Fact]
    public void DroppingStepsIsDetectedAsLessActivity()
    {
        var steps = Enumerable.Range(0, 14).ToDictionary(day => day, day => day < 7 ? 9_000d : 4_000d);
        var snapshot = Snapshot(events: Sessions("user-1", Enumerable.Range(0, 14).ToArray()), steps: steps);

        var risk = analyzer.Analyze(snapshot, Day(13));

        Assert.Contains(risk.Signals, signal => signal.Code == "less_activity");
    }

    [Fact]
    public void FewerCompletedWorkoutsIsDetectedAsLessActivity()
    {
        var snapshot = Snapshot(
            events: Sessions("user-1", Enumerable.Range(0, 14).ToArray()),
            workouts: [Workout(1, 1), Workout(3, 3), Workout(5, 5), Workout(9), Workout(11)]);

        var risk = analyzer.Analyze(snapshot, Day(13));

        var signal = Assert.Single(risk.Signals, item => item.Code == "less_activity");
        Assert.Equal((3, 0), (signal.Previous, signal.Current));
    }

    [Fact]
    public void FewerInteractionsIsDetected()
    {
        var events = Sessions("user-1", Enumerable.Range(0, 14).ToArray())
            .Concat(new[] { 1, 2, 3, 4 }.Select(day => Event("user-1", ProductEventNames.CoachQuery, day)))
            .Concat(new[] { 10 }.Select(day => Event("user-1", ProductEventNames.MealLogged, day)));

        var risk = analyzer.Analyze(Snapshot(events: events), Day(13));

        Assert.Contains(risk.Signals, signal => signal.Code == "less_interaction");
    }

    [Fact]
    public void UserWhoStoppedCompletelyIsHighRisk()
    {
        var events = Sessions("user-1", 0, 1, 2, 3, 4, 5)
            .Concat(new[] { 1, 2, 3 }.Select(day => Event("user-1", ProductEventNames.CoachQuery, day)));

        var risk = analyzer.Analyze(Snapshot(events: events), Day(13));

        Assert.Equal(DropoutRiskLevel.High, risk.Level);
        Assert.Equal(8, risk.DaysSinceLastActive);
        Assert.Contains(risk.Signals, signal => signal.Code == "inactive");
        Assert.Contains(risk.Signals, signal => signal.Code == "less_usage");
        Assert.Contains(risk.Signals, signal => signal.Code == "less_interaction");
    }

    [Fact]
    public void NewUsersAreNotComparedAgainstAWeekTheyDidNotHave()
    {
        var risk = analyzer.Analyze(Snapshot(events: Sessions("user-1", 10, 11, 12, 13)), Day(13));

        Assert.False(risk.HasEnoughHistory);
        Assert.Equal(DropoutRiskLevel.Low, risk.Level);
        Assert.Empty(risk.Signals);
    }

    [Fact]
    public void NewUserWhoDisappearsIsStillFlaggedAsInactive()
    {
        var risk = analyzer.Analyze(Snapshot(events: Sessions("user-1", 5)), Day(13));

        Assert.False(risk.HasEnoughHistory);
        Assert.Contains(risk.Signals, signal => signal.Code == "inactive");
        Assert.Equal(DropoutRiskLevel.Medium, risk.Level);
    }

    [Fact]
    public void UserWithoutAnyActivityHasNoRiskData()
    {
        var risk = analyzer.Analyze(Snapshot(), Day(13));

        Assert.Null(risk.DaysSinceLastActive);
        Assert.Equal(0, risk.Score);
    }
}
