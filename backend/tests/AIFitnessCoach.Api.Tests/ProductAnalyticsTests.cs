using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;
using Xunit;
using static AIFitnessCoach.Api.Tests.AnalyticsTestData;

namespace AIFitnessCoach.Api.Tests;

/// <summary>Product metrics, motivation (streaks, achievements, weekly summary) and insights.</summary>
public sealed class ProductAnalyticsTests
{
    [Fact]
    public void MetricsCountActiveUsersSessionsAndKeyActions()
    {
        var events = Sessions("a", 0, 1, 1, 6)
            .Concat(Sessions("b", 1))
            .Concat(Sessions("c", 10)) // outside the period
            .Concat(
            [
                Event("a", ProductEventNames.WorkoutStarted, 1),
                Event("a", ProductEventNames.WorkoutCompleted, 1, new Dictionary<string, string> { ["workout_day_id"] = "d1" }),
                Event("a", ProductEventNames.WorkoutCompleted, 1, new Dictionary<string, string> { ["workout_day_id"] = "d1" }),
                Event("a", ProductEventNames.WorkoutFeedbackSubmitted, 1),
                Event("b", ProductEventNames.CoachQuery, 1, new Dictionary<string, string> { ["client_request_id"] = "r1" }),
                Event("b", ProductEventNames.CoachQuery, 1, new Dictionary<string, string> { ["client_request_id"] = "r1" }),
                Event("b", ProductEventNames.CoachQuery, 1, new Dictionary<string, string> { ["client_request_id"] = "r2" }),
                Event("b", ProductEventNames.GoalCreated, 1),
                Event("b", ProductEventNames.GoalCompleted, 6),
                Event("a", ProductEventNames.MealLogged, 6)
            ])
            .ToArray();

        var metrics = new ProductMetricsCalculator().Calculate(events, Day(0), Day(6));

        Assert.Equal(2, metrics.TotalUsers);
        Assert.Equal([1, 2, 0, 0, 0, 0, 2], metrics.Daily.Select(day => day.Users));
        Assert.Equal(2, metrics.WeeklyActiveUsers);
        Assert.Equal(5, metrics.Sessions);
        Assert.Equal(1, metrics.WorkoutsStarted);
        Assert.Equal(1, metrics.WorkoutsCompleted);
        Assert.Equal(1, metrics.FeedbackSubmitted);
        Assert.Equal(2, metrics.CoachQueries);
        Assert.Equal(1, metrics.GoalsCreated);
        Assert.Equal(1, metrics.GoalsCompleted);
        Assert.Equal(1, metrics.MealsLogged);
        Assert.Equal(2.5, metrics.SessionsPerActiveUser);
    }

    [Fact]
    public void StreakCountsConsecutiveProgressDaysAndSurvivesUntilTheDayEnds()
    {
        var days = new HashSet<DateOnly> { Day(0), Day(1), Day(2), Day(5), Day(6) };

        var today = MotivationEngine.CalculateStreak(days, Day(6));
        var nextMorning = MotivationEngine.CalculateStreak(days, Day(7));
        var broken = MotivationEngine.CalculateStreak(days, Day(8));

        Assert.Equal((2, 3, true), (today.Current, today.Longest, today.ActiveToday));
        Assert.Equal((2, false), (nextMorning.Current, nextMorning.ActiveToday));
        Assert.Equal(0, broken.Current);
    }

    [Fact]
    public void AchievementsUnlockWithTheirDateAndShowProgress()
    {
        var snapshot = Snapshot(
            workouts: [Workout(0, 0), Workout(1, 1), Workout(2, 2)],
            events: [Event("user-1", ProductEventNames.CoachQuery, 1)],
            goals: [Goal(0)]);

        var achievements = new MotivationEngine().Build(snapshot, At(2), TimeSpan.Zero).Achievements
            .ToDictionary(item => item.Id);

        Assert.True(achievements["first_workout"].Unlocked);
        Assert.Equal(Day(0), achievements["first_workout"].UnlockedOn);
        Assert.False(achievements["workouts_10"].Unlocked);
        Assert.Equal((3, 10), (achievements["workouts_10"].Progress, achievements["workouts_10"].Target));
        Assert.True(achievements["streak_3"].Unlocked);
        Assert.Equal(Day(2), achievements["streak_3"].UnlockedOn);
        Assert.False(achievements["streak_7"].Unlocked);
        Assert.True(achievements["first_coach_query"].Unlocked);
        Assert.True(achievements["first_goal"].Unlocked);
        Assert.False(achievements["goal_completed"].Unlocked);
    }

    [Fact]
    public void WeeklySummaryComparesWithThePreviousWeek()
    {
        var snapshot = Snapshot(
            workouts: [Workout(1, 1), Workout(9, 9), Workout(10, 10), Workout(12)],
            mealDays: [9, 11],
            events: [Event("user-1", ProductEventNames.MealLogged, 9), Event("user-1", ProductEventNames.MealLogged, 11)],
            steps: new Dictionary<int, double> { [9] = 8_000, [10] = 6_000 });

        var summary = new MotivationEngine().Build(snapshot, At(13), TimeSpan.Zero).WeeklySummary;

        Assert.Equal((Day(7), Day(13)), (summary.From, summary.To));
        Assert.Equal(2, summary.WorkoutsCompleted);
        Assert.Equal(3, summary.WorkoutsPlanned);
        Assert.Equal(1, summary.PreviousWeekWorkoutsCompleted);
        Assert.Equal(2, summary.MealsLogged);
        Assert.Equal(7_000, summary.AverageDailySteps);
        Assert.Contains(summary.Highlights, item => item.Contains("2 entrenamientos de 3 planificados"));
        Assert.Contains(summary.Highlights, item => item.Contains("1 más que la semana anterior"));
    }

    [Fact]
    public void InsightsRankFeatureUsage()
    {
        var events = new[]
        {
            Event("a", ProductEventNames.CoachQuery, 1),
            Event("b", ProductEventNames.CoachQuery, 1),
            Event("b", ProductEventNames.CoachQuery, 1, new Dictionary<string, string> { ["kind"] = "analysis" }),
            Event("a", ProductEventNames.MealLogged, 2),
            Event("a", ProductEventNames.ScreenView, 2, new Dictionary<string, string> { ["screen"] = "progress" }),
            Event("c", ProductEventNames.SessionStart, 2)
        };

        var report = new ProductInsights().Analyze(events, [], new Dictionary<string, IReadOnlyList<DateTimeOffset>>(), Day(0), Day(6));

        Assert.Equal(3, report.ActiveUsers);
        Assert.Equal("coach", report.MostUsedFeatures[0].Feature);
        Assert.Equal(2, report.MostUsedFeatures[0].Users);
        Assert.Equal(0.667, report.MostUsedFeatures[0].ShareOfActiveUsers);
        Assert.Contains(report.MostUsedFeatures, item => item.Feature == "activity_analysis");
        Assert.Contains(report.MostUsedFeatures, item => item.Feature == "progress");
    }

    [Fact]
    public void InsightsDetectFeaturesAbandonedByUsersWhoStayed()
    {
        // As of day 30 the window is days 17–30. Both used the Coach early; only "a" still does.
        var events = new[]
        {
            Event("a", ProductEventNames.CoachQuery, 1),
            Event("b", ProductEventNames.CoachQuery, 2),
            Event("a", ProductEventNames.CoachQuery, 25),
            Event("b", ProductEventNames.SessionStart, 26),
            Event("c", ProductEventNames.CoachQuery, 3) // churned from the app entirely: not "abandoned the feature"
        };

        var report = new ProductInsights().Analyze(events, [], new Dictionary<string, IReadOnlyList<DateTimeOffset>>(), Day(0), Day(30));

        var coach = Assert.Single(report.AbandonedFeatures, item => item.Feature == "coach");
        Assert.Equal((2, 1, 0.5), (coach.Adopters, coach.Abandoned, coach.Rate));
    }

    [Fact]
    public void InsightsRankRecommendationTypesByFollowThrough()
    {
        var recommendations = new[]
        {
            new RecommendationFact(Guid.NewGuid(), "a", "ReduceVolume", At(1)),
            new RecommendationFact(Guid.NewGuid(), "b", "ReduceVolume", At(1)),
            new RecommendationFact(Guid.NewGuid(), "a", "Progress", At(2)),
            new RecommendationFact(Guid.NewGuid(), "a", "TooRecent", At(28)) // window not finished yet
        };
        var completions = new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["a"] = [At(3), At(4), At(20)],
            ["b"] = [At(12)] // more than 7 days later
        };

        var report = new ProductInsights().Analyze([], recommendations, completions, Day(0), Day(30));

        Assert.Equal(["Progress", "ReduceVolume"], report.RecommendationsByAdherence.Select(item => item.Type));
        var progress = report.RecommendationsByAdherence[0];
        Assert.Equal((1, 1, 1.0, 2.0), (progress.Recommendations, progress.FollowedByWorkout, progress.FollowThroughRate, progress.AverageWorkoutsWithinSevenDays));
        Assert.Equal(0.5, report.RecommendationsByAdherence[1].FollowThroughRate);
    }
}
