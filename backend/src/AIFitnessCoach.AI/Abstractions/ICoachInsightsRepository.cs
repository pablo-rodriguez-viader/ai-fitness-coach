using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Contracts;

namespace AIFitnessCoach.AI.Abstractions;

public interface ICoachInsightsRepository
{
    Task SaveAnalysisAsync(
        UserInsights insights,
        IReadOnlyList<BehaviorPattern> patterns,
        TrainingAdherence adherence,
        IReadOnlyList<GoalAchievement> achievements,
        CancellationToken cancellationToken);
    Task AddRecommendationAsync(RecommendationHistory recommendation, CancellationToken cancellationToken);
    Task AddOutcomeAsync(RecommendationOutcome outcome, CancellationToken cancellationToken);
    Task<IReadOnlyList<AIHealthMetricContext>> GetTrendMetricsAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken);
    Task<RecommendationHistory?> GetRecommendationAsync(
        string userId,
        Guid recommendationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BehaviorPattern>> GetPatternsAsync(string userId, CancellationToken cancellationToken);
    Task<TrainingAdherence?> GetLatestAdherenceAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoalAchievement>> GetAchievementsAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecommendationHistory>> GetRecommendationsAsync(string userId, int count, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecommendationOutcome>> GetOutcomesAsync(
        string userId,
        int count,
        CancellationToken cancellationToken);
}
