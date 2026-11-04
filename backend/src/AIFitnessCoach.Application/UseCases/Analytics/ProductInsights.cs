using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Analytics;

public sealed record FeatureUsage(string Feature, int Users, int Uses, double ShareOfActiveUsers);

/// <param name="Adopters">Users who first used the feature at least 14 days ago and still use the app.</param>
/// <param name="Abandoned">Adopters who kept using the app but not this feature in the last 14 days.</param>
public sealed record FeatureAbandonment(string Feature, int Adopters, int Abandoned, double? Rate);

/// <param name="FollowThroughRate">Share of recommendations followed by a completed workout within 7 days.</param>
public sealed record RecommendationEffect(
    string Type,
    int Recommendations,
    int FollowedByWorkout,
    double? FollowThroughRate,
    double AverageWorkoutsWithinSevenDays);

public sealed record ProductInsightsReport(
    DateOnly From,
    DateOnly To,
    int ActiveUsers,
    IReadOnlyList<FeatureUsage> MostUsedFeatures,
    IReadOnlyList<FeatureAbandonment> AbandonedFeatures,
    IReadOnlyList<RecommendationEffect> RecommendationsByAdherence);

/// <summary>Answers: which features are used most, which get abandoned, and which recommendation
/// types are followed by actual training. Associations only; they do not prove causation.</summary>
public sealed class ProductInsights
{
    public const int AbandonmentWindowDays = 14;
    public const int RecommendationWindowDays = 7;

    public static readonly IReadOnlyList<string> Features =
        ["coach", "activity_analysis", "workouts", "nutrition", "goals", "progress", "weekly_summary", "achievements", "notifications"];

    /// <summary>Maps an event to the feature it belongs to, or null for app-level events.</summary>
    public static string? FeatureOf(AnalyticsEvent item) => item.Name switch
    {
        ProductEventNames.CoachQuery => item.Property("kind") == "analysis" ? "activity_analysis" : "coach",
        ProductEventNames.WorkoutStarted or ProductEventNames.WorkoutCompleted
            or ProductEventNames.WorkoutFeedbackSubmitted or ProductEventNames.WorkoutPlanGenerated => "workouts",
        ProductEventNames.MealLogged => "nutrition",
        ProductEventNames.GoalCreated or ProductEventNames.GoalCompleted => "goals",
        ProductEventNames.NotificationOpened => "notifications",
        ProductEventNames.ScreenView => item.Property("screen") switch
        {
            "coach" => "coach",
            "activity_analysis" => "activity_analysis",
            "workouts" => "workouts",
            "nutrition" => "nutrition",
            "goals" => "goals",
            "progress" => "progress",
            "weekly_summary" => "weekly_summary",
            "achievements" or "streak" => "achievements",
            _ => null
        },
        _ => null
    };

    public ProductInsightsReport Analyze(
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<RecommendationFact> recommendations,
        IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> completedWorkoutsByUser,
        DateOnly from,
        DateOnly to)
    {
        static DateOnly Day(AnalyticsEvent item) => AnalyticsCalendar.Day(item.OccurredAt, TimeSpan.Zero);
        var inRange = events.Where(item => Day(item) >= from && Day(item) <= to).ToArray();
        var activeUsers = inRange.Select(item => item.UserId).Distinct().Count();

        var usage = inRange
            .Select(item => (Feature: FeatureOf(item), item.UserId))
            .Where(pair => pair.Feature is not null)
            .GroupBy(pair => pair.Feature!)
            .Select(group => new FeatureUsage(
                group.Key,
                group.Select(pair => pair.UserId).Distinct().Count(),
                group.Count(),
                activeUsers == 0 ? 0 : Math.Round(group.Select(pair => pair.UserId).Distinct().Count() / (double)activeUsers, 3)))
            .OrderByDescending(item => item.Users)
            .ThenByDescending(item => item.Uses)
            .ToArray();

        var windowStart = to.AddDays(-(AbandonmentWindowDays - 1));
        var recentlyActive = events
            .Where(item => Day(item) >= windowStart && Day(item) <= to)
            .Select(item => item.UserId)
            .ToHashSet();
        var featureEvents = events
            .Where(item => Day(item) <= to)
            .Select(item => (Feature: FeatureOf(item), item.UserId, Day: Day(item)))
            .Where(item => item.Feature is not null)
            .ToArray();
        var abandonment = Features
            .Select(feature =>
            {
                var byUser = featureEvents
                    .Where(item => item.Feature == feature)
                    .GroupBy(item => item.UserId)
                    .ToArray();
                var adopters = byUser
                    .Where(group => group.Min(item => item.Day) < windowStart && recentlyActive.Contains(group.Key))
                    .ToArray();
                var abandoned = adopters.Count(group => group.Max(item => item.Day) < windowStart);
                return new FeatureAbandonment(
                    feature,
                    adopters.Length,
                    abandoned,
                    adopters.Length == 0 ? null : Math.Round(abandoned / (double)adopters.Length, 3));
            })
            .Where(item => item.Adopters > 0)
            .OrderByDescending(item => item.Rate)
            .ToArray();

        var latestMeasurable = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var recommendationEffects = recommendations
            .Where(item => item.CreatedAt.UtcDateTime.AddDays(RecommendationWindowDays) <= latestMeasurable)
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .Select(group =>
            {
                var followUps = group
                    .Select(recommendation =>
                    {
                        var completions = completedWorkoutsByUser.TryGetValue(recommendation.UserId, out var list)
                            ? list
                            : [];
                        var windowEnd = recommendation.CreatedAt.AddDays(RecommendationWindowDays);
                        return completions.Count(at => at > recommendation.CreatedAt && at <= windowEnd);
                    })
                    .ToArray();
                var followed = followUps.Count(count => count > 0);
                return new RecommendationEffect(
                    group.Key,
                    followUps.Length,
                    followed,
                    followUps.Length == 0 ? null : Math.Round(followed / (double)followUps.Length, 3),
                    followUps.Length == 0 ? 0 : Math.Round(followUps.Average(), 2));
            })
            .OrderByDescending(item => item.FollowThroughRate)
            .ThenByDescending(item => item.Recommendations)
            .ToArray();

        return new ProductInsightsReport(from, to, activeUsers, usage, abandonment, recommendationEffects);
    }
}
