using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Models;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class WorkoutRepository(FitnessDbContext dbContext) : IWorkoutRepository
{
    public async Task SavePlanAsync(WorkoutPlan plan, CancellationToken cancellationToken)
    {
        dbContext.WorkoutPlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<WorkoutPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.WorkoutPlans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId)
            .OrderByDescending(plan => plan.CreatedAt)
            .Include(plan => plan.Days.OrderBy(day => day.Date))
            .ThenInclude(day => day.Exercises.OrderBy(exercise => exercise.Position))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkoutPlan>> GetPlansAsync(
        string userId,
        int count,
        CancellationToken cancellationToken) =>
        await dbContext.WorkoutPlans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId)
            .OrderByDescending(plan => plan.CreatedAt)
            .Take(Math.Clamp(count, 1, 100))
            .Include(plan => plan.Days.OrderBy(day => day.Date))
            .ThenInclude(day => day.Exercises.OrderBy(exercise => exercise.Position))
            .ToArrayAsync(cancellationToken);

    public Task<WorkoutPlan?> GetOwnedPlanAsync(string userId, Guid planId, CancellationToken cancellationToken) =>
        dbContext.WorkoutPlans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId && plan.Id == planId)
            .Include(plan => plan.Days.OrderBy(day => day.Date))
            .ThenInclude(day => day.Exercises.OrderBy(exercise => exercise.Position))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkoutHistory>> GetHistoryAsync(
        string userId,
        int count,
        CancellationToken cancellationToken)
    {
        var rows = await (from completion in dbContext.WorkoutCompletions.AsNoTracking()
               where completion.UserId == userId
               join day in dbContext.WorkoutDays.AsNoTracking() on completion.WorkoutDayId equals day.Id
               join plan in dbContext.WorkoutPlans.AsNoTracking() on day.WorkoutPlanId equals plan.Id
               let difficulty = dbContext.WorkoutFeedback
                   .Where(feedback => feedback.UserId == userId && feedback.WorkoutDayId == day.Id)
                   .OrderByDescending(feedback => feedback.CreatedAt)
                   .Select(feedback => (WorkoutDifficulty?)feedback.Difficulty)
                   .FirstOrDefault()
               orderby completion.RecordedAt descending
               select new
               {
                   completion.Id,
                   WorkoutPlanId = plan.Id,
                   WorkoutDayId = day.Id,
                   plan.Goal,
                   day.Date,
                   day.Title,
                   completion.Completed,
                   difficulty,
                   completion.RecordedAt })
            .Take(Math.Clamp(count, 1, 500))
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => new WorkoutHistory(
            row.Id,
            row.WorkoutPlanId,
            row.WorkoutDayId,
            row.Goal,
            row.Date,
            row.Title,
            row.Completed,
            row.difficulty?.ToString(),
            row.RecordedAt)).ToArray();
    }

    public Task<WorkoutPlan?> GetOwnedPlanByDayAsync(
        string userId,
        Guid workoutDayId,
        CancellationToken cancellationToken) =>
        dbContext.WorkoutPlans
            .Where(plan => plan.UserId == userId && plan.Days.Any(day => day.Id == workoutDayId))
            .Include(plan => plan.Days)
            .ThenInclude(day => day.Exercises)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddFeedbackAsync(
        WorkoutFeedback feedback,
        WorkoutCompletion completion,
        CancellationToken cancellationToken)
    {
        dbContext.WorkoutFeedback.Add(feedback);
        dbContext.WorkoutCompletions.Add(completion);
        var recommendationId = await (
            from day in dbContext.WorkoutDays
            join plan in dbContext.WorkoutPlans on day.WorkoutPlanId equals plan.Id
            join recommendation in dbContext.RecommendationHistory
                on plan.Id equals recommendation.WorkoutPlanId
            where day.Id == feedback.WorkoutDayId
                && plan.UserId == feedback.UserId
                && recommendation.UserId == feedback.UserId
            orderby recommendation.CreatedAt descending
            select (Guid?)recommendation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (recommendationId is { } id)
        {
            var score = !feedback.Completed
                ? 0.1
                : feedback.Difficulty switch
                {
                    WorkoutDifficulty.Hard or WorkoutDifficulty.VeryHard => 0.25,
                    WorkoutDifficulty.VeryEasy or WorkoutDifficulty.Easy => 0.7,
                    _ => 0.9
                };
            dbContext.RecommendationOutcomes.Add(RecommendationOutcome.Create(
                feedback.UserId,
                id,
                feedback.Completed,
                $"workout_feedback:{feedback.Difficulty}",
                score,
                feedback.CreatedAt));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddCompletionAsync(WorkoutCompletion completion, CancellationToken cancellationToken)
    {
        dbContext.WorkoutCompletions.Add(completion);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkoutFeedback>> GetRecentFeedbackAsync(
        string userId,
        int count,
        CancellationToken cancellationToken) =>
        await dbContext.WorkoutFeedback
            .AsNoTracking()
            .Where(feedback => feedback.UserId == userId)
            .OrderByDescending(feedback => feedback.CreatedAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkoutRecommendation>> GetRecommendationsAsync(
        string userId,
        int count,
        CancellationToken cancellationToken) =>
        await dbContext.WorkoutRecommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.UserId == userId)
            .OrderByDescending(recommendation => recommendation.CreatedAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToArrayAsync(cancellationToken);

    public async Task AddRecommendationAsync(
        WorkoutRecommendation recommendation,
        CancellationToken cancellationToken)
    {
        dbContext.WorkoutRecommendations.Add(recommendation);
        dbContext.RecommendationHistory.Add(RecommendationHistory.Create(
            recommendation.UserId,
            recommendation.WorkoutPlanId,
            recommendation.Type,
            recommendation.Text,
            recommendation.CreatedAt));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
