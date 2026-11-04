namespace AIFitnessCoach.AI.Contracts;

public sealed record BehaviorPatternView(
    string Key,
    string Description,
    double Confidence,
    int EvidenceCount);

public sealed record MetricTrendView(string Metric, double? Change, string Unit);

public sealed record TrainingAdherenceView(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int PlannedSessions,
    int CompletedSessions,
    double Rate);

public sealed record GoalAchievementView(
    Guid GoalId,
    string Goal,
    string Status,
    double? ProgressPercent);

public sealed record RecommendationEffectiveness(
    string Type,
    int OutcomeCount,
    double AverageScore,
    double SuccessRate);

public sealed record RecommendationHistoryView(
    Guid Id,
    Guid? WorkoutPlanId,
    string Type,
    string Text,
    DateTimeOffset CreatedAt,
    double? OutcomeScore,
    string? Feedback);

public sealed record CoachInsightsView(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<BehaviorPatternView> Patterns,
    TrainingAdherenceView Adherence,
    IReadOnlyList<MetricTrendView> Trends,
    IReadOnlyList<GoalAchievementView> Goals,
    IReadOnlyList<RecommendationEffectiveness> EffectiveRecommendations);

public sealed record ProgressAnalysisView(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<MetricTrendView> Trends,
    IReadOnlyList<GoalAchievementView> Goals,
    string Summary);

public sealed record RecommendationFeedback(
    Guid RecommendationId,
    int Score,
    string? Comment);

public readonly record struct RecommendationScore
{
    private RecommendationScore(double value) => Value = value;

    public double Value { get; }
    public bool IsEffective => Value >= 0.6;

    public static RecommendationScore FromRating(int rating)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Recommendation rating must be from 1 to 5.");
        return new RecommendationScore(rating / 5d);
    }

    public static RecommendationScore FromNormalized(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(value));
        return new RecommendationScore(value);
    }
}
