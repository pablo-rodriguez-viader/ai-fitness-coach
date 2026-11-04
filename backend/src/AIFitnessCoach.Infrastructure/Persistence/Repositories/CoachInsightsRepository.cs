using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Models;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class CoachInsightsRepository(FitnessDbContext dbContext) : ICoachInsightsRepository
{
    public async Task SaveAnalysisAsync(
        UserInsights insights,
        IReadOnlyList<BehaviorPattern> patterns,
        TrainingAdherence adherence,
        IReadOnlyList<GoalAchievement> achievements,
        CancellationToken cancellationToken)
    {
        var existingInsight = await dbContext.UserInsights
            .Where(item => item.UserId == insights.UserId && item.Date == insights.Date)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingInsight is null)
            dbContext.UserInsights.Add(insights);
        else
            existingInsight.UpdateFrom(insights);

        var patternKeys = patterns.Select(item => item.Key).ToArray();
        var existingPatterns = await dbContext.BehaviorPatterns
            .Where(item => item.UserId == insights.UserId)
            .ToArrayAsync(cancellationToken);
        dbContext.BehaviorPatterns.RemoveRange(existingPatterns.Where(item => !patternKeys.Contains(item.Key)));
        foreach (var pattern in patterns)
        {
            var existingPattern = existingPatterns.SingleOrDefault(item => item.Key == pattern.Key);
            if (existingPattern is null)
                dbContext.BehaviorPatterns.Add(pattern);
            else
                existingPattern.UpdateFrom(pattern);
        }

        var existingAdherence = await dbContext.TrainingAdherence
            .Where(item => item.UserId == adherence.UserId && item.PeriodStart == adherence.PeriodStart)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingAdherence is null)
            dbContext.TrainingAdherence.Add(adherence);
        else
            existingAdherence.UpdateFrom(adherence);

        var goalIds = achievements.Select(item => item.GoalId).ToArray();
        var existingAchievements = await dbContext.GoalAchievements
            .Where(item => item.UserId == insights.UserId)
            .ToArrayAsync(cancellationToken);
        dbContext.GoalAchievements.RemoveRange(existingAchievements.Where(item => !goalIds.Contains(item.GoalId)));
        foreach (var achievement in achievements)
        {
            var existingAchievement = existingAchievements.SingleOrDefault(item => item.GoalId == achievement.GoalId);
            if (existingAchievement is null)
                dbContext.GoalAchievements.Add(achievement);
            else
                existingAchievement.UpdateFrom(achievement);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRecommendationAsync(
        RecommendationHistory recommendation,
        CancellationToken cancellationToken)
    {
        dbContext.RecommendationHistory.Add(recommendation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddOutcomeAsync(RecommendationOutcome outcome, CancellationToken cancellationToken)
    {
        dbContext.RecommendationOutcomes.Add(outcome);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AIFitnessCoach.AI.Contracts.AIHealthMetricContext>> GetTrendMetricsAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        var types = new[]
        {
            "body_mass",
            "steps",
            "sleep_duration",
            "heart_rate",
            "resting_heart_rate",
            "active_energy",
            "basal_energy",
            "total_energy"
        };
        var metrics = new List<AIFitnessCoach.AI.Contracts.AIHealthMetricContext>();
        foreach (var type in types)
        {
            var rows = await dbContext.HealthMetrics
                .AsNoTracking()
                .Where(item => item.UserId == userId && item.Metric == type && item.ObservedAt >= from)
                .OrderByDescending(item => item.ObservedAt)
                .Take(5000)
                .Select(item => new AIFitnessCoach.AI.Contracts.AIHealthMetricContext(
                    item.Metric,
                    item.Value,
                    item.Unit,
                    item.ObservedAt))
                .ToArrayAsync(cancellationToken);
            metrics.AddRange(rows);
        }
        return metrics;
    }

    public Task<RecommendationHistory?> GetRecommendationAsync(
        string userId,
        Guid recommendationId,
        CancellationToken cancellationToken) =>
        dbContext.RecommendationHistory
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.Id == recommendationId,
                cancellationToken);

    public async Task<IReadOnlyList<BehaviorPattern>> GetPatternsAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await dbContext.BehaviorPatterns
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Key)
            .ToArrayAsync(cancellationToken);

    public Task<TrainingAdherence?> GetLatestAdherenceAsync(
        string userId,
        CancellationToken cancellationToken) =>
        dbContext.TrainingAdherence
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<GoalAchievement>> GetAchievementsAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await dbContext.GoalAchievements
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<RecommendationHistory>> GetRecommendationsAsync(
        string userId,
        int count,
        CancellationToken cancellationToken) =>
        await dbContext.RecommendationHistory
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<RecommendationOutcome>> GetOutcomesAsync(
        string userId,
        int count,
        CancellationToken cancellationToken) =>
        await dbContext.RecommendationOutcomes
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Clamp(count, 1, 500))
            .ToArrayAsync(cancellationToken);
}
