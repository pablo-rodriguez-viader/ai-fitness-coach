using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class CoachInsightsService(
    IWorkoutRepository workoutRepository,
    IWellnessRepository wellnessRepository,
    ICoachInsightsRepository insightsRepository,
    TrendAnalyzer trendAnalyzer,
    AdherenceAnalyzer adherenceAnalyzer,
    BehaviorAnalyzer behaviorAnalyzer,
    GoalAchievementAnalyzer goalAchievementAnalyzer,
    ProgressAnalyzer progressAnalyzer,
    TimeProvider timeProvider)
{
    public async Task<CoachInsightsView> GetInsightsAsync(string userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var trendMetrics = await insightsRepository.GetTrendMetricsAsync(userId, now.AddDays(-90), cancellationToken);
        var plans = await workoutRepository.GetPlansAsync(userId, 100, cancellationToken);
        var history = await workoutRepository.GetHistoryAsync(userId, 500, cancellationToken);
        var feedback = await workoutRepository.GetRecentFeedbackAsync(userId, 100, cancellationToken);
        var goals = await wellnessRepository.GetGoalsAsync(userId, cancellationToken);
        var trends = trendAnalyzer.Analyze(trendMetrics, now);
        var adherence = adherenceAnalyzer.Analyze(userId, plans, history, now);
        var patterns = behaviorAnalyzer.Analyze(userId, adherence, trends, feedback, now);
        var goalViews = goalAchievementAnalyzer.Analyze(goals, trendMetrics);
        var achievements = goalViews.Select(item => GoalAchievement.Create(
            userId,
            item.GoalId,
            item.Goal,
            item.Status,
            item.ProgressPercent,
            now)).ToArray();
        var trendMap = trends.ToDictionary(item => item.Metric, item => item.Change);
        var summary = patterns.Count == 0
            ? "Aún no hay suficiente historial para detectar patrones de comportamiento."
            : string.Join(" ", patterns.Select(item => item.Description));
        var insights = UserInsights.Create(
            userId,
            adherence.PeriodEnd,
            trendMap.GetValueOrDefault("weight"),
            trendMap.GetValueOrDefault("sleep"),
            trendMap.GetValueOrDefault("steps"),
            trendMap.GetValueOrDefault("heart_rate"),
            trendMap.GetValueOrDefault("active_calories"),
            trendMap.GetValueOrDefault("total_calories"),
            adherence.Rate,
            summary,
            now);

        await insightsRepository.SaveAnalysisAsync(insights, patterns, adherence, achievements, cancellationToken);
        var recommendations = await insightsRepository.GetRecommendationsAsync(userId, 100, cancellationToken);
        var outcomes = await insightsRepository.GetOutcomesAsync(userId, 500, cancellationToken);
        return new CoachInsightsView(
            now,
            patterns.Select(item => new BehaviorPatternView(
                item.Key, item.Description, item.Confidence, item.EvidenceCount)).ToArray(),
            new TrainingAdherenceView(
                adherence.PeriodStart,
                adherence.PeriodEnd,
                adherence.PlannedSessions,
                adherence.CompletedSessions,
                adherence.Rate),
            trends,
            goalViews,
            AnalyzeEffectiveness(recommendations, outcomes));
    }

    public async Task<ProgressAnalysisView> GetProgressAnalysisAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var trendMetrics = await insightsRepository.GetTrendMetricsAsync(userId, now.AddDays(-90), cancellationToken);
        var goals = await wellnessRepository.GetGoalsAsync(userId, cancellationToken);
        var trends = trendAnalyzer.Analyze(trendMetrics, now);
        var goalViews = goalAchievementAnalyzer.Analyze(goals, trendMetrics);
        var summary = progressAnalyzer.AnalyzeCoachProgress(trends, goalViews);
        return new ProgressAnalysisView(now, trends, goalViews, summary);
    }

    public async Task<TrainingAdherenceView> GetAdherenceAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var plans = await workoutRepository.GetPlansAsync(userId, 100, cancellationToken);
        var history = await workoutRepository.GetHistoryAsync(userId, 500, cancellationToken);
        var adherence = adherenceAnalyzer.Analyze(userId, plans, history, now);
        return new TrainingAdherenceView(
            adherence.PeriodStart,
            adherence.PeriodEnd,
            adherence.PlannedSessions,
            adherence.CompletedSessions,
            adherence.Rate);
    }

    public async Task<IReadOnlyList<RecommendationHistoryView>> GetRecommendationHistoryAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var recommendations = await insightsRepository.GetRecommendationsAsync(userId, 100, cancellationToken);
        var outcomes = await insightsRepository.GetOutcomesAsync(userId, 500, cancellationToken);
        var latestOutcomes = outcomes.GroupBy(item => item.RecommendationHistoryId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CreatedAt).First());
        return recommendations.Select(item =>
        {
            latestOutcomes.TryGetValue(item.Id, out var outcome);
            return new RecommendationHistoryView(
                item.Id,
                item.WorkoutPlanId,
                item.Type,
                item.Text,
                item.CreatedAt,
                outcome?.Score,
                outcome?.Feedback);
        }).ToArray();
    }

    public async Task RecordRecommendationFeedbackAsync(
        string userId,
        RecommendationFeedback request,
        CancellationToken cancellationToken)
    {
        if (request.RecommendationId == Guid.Empty || request.Score is < 1 or > 5
            || request.Comment?.Length > 500)
            throw new ArgumentException("A recommendation and a score from 1 to 5 are required.", nameof(request));
        var recommendation = await insightsRepository.GetRecommendationAsync(
            userId,
            request.RecommendationId,
            cancellationToken);
        if (recommendation is null)
            throw new RecommendationNotFoundException();
        var now = timeProvider.GetUtcNow();
        var score = RecommendationScore.FromRating(request.Score).Value;
        var feedback = string.IsNullOrWhiteSpace(request.Comment)
            ? $"user_score:{request.Score}"
            : request.Comment.Trim();
        await insightsRepository.AddOutcomeAsync(
            RecommendationOutcome.Create(
                userId,
                recommendation.Id,
                null,
                feedback,
                score,
                now),
            cancellationToken);
    }

    private static IReadOnlyList<RecommendationEffectiveness> AnalyzeEffectiveness(
        IReadOnlyList<RecommendationHistory> recommendations,
        IReadOnlyList<RecommendationOutcome> outcomes)
    {
        var recommendationById = recommendations.ToDictionary(item => item.Id);
        return outcomes
            .GroupBy(item => item.RecommendationHistoryId)
            .Select(group => group.OrderByDescending(item => item.CreatedAt).First())
            .Where(outcome => recommendationById.ContainsKey(outcome.RecommendationHistoryId))
            .GroupBy(outcome => recommendationById[outcome.RecommendationHistoryId].Type)
            .Select(group => new RecommendationEffectiveness(
                group.Key,
                group.Count(),
                Math.Round(group.Average(item => item.Score), 2, MidpointRounding.AwayFromZero),
                Math.Round(group.Count(item => RecommendationScore.FromNormalized(item.Score).IsEffective)
                    / (double)group.Count(), 2, MidpointRounding.AwayFromZero)))
            .OrderByDescending(item => item.AverageScore)
            .ToArray();
    }
}

public sealed class RecommendationNotFoundException : Exception;
