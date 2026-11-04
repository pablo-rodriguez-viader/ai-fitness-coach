using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Abstractions;

public interface IWorkoutRepository
{
    Task SavePlanAsync(WorkoutPlan plan, CancellationToken cancellationToken);
    Task<WorkoutPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkoutPlan>> GetPlansAsync(string userId, int count, CancellationToken cancellationToken);
    Task<WorkoutPlan?> GetOwnedPlanAsync(string userId, Guid planId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkoutHistory>> GetHistoryAsync(
        string userId,
        int count,
        CancellationToken cancellationToken);
    Task<WorkoutPlan?> GetOwnedPlanByDayAsync(string userId, Guid workoutDayId, CancellationToken cancellationToken);
    Task AddFeedbackAsync(
        WorkoutFeedback feedback,
        WorkoutCompletion completion,
        CancellationToken cancellationToken);
    Task AddCompletionAsync(WorkoutCompletion completion, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkoutFeedback>> GetRecentFeedbackAsync(
        string userId,
        int count,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkoutRecommendation>> GetRecommendationsAsync(
        string userId,
        int count,
        CancellationToken cancellationToken);
    Task AddRecommendationAsync(WorkoutRecommendation recommendation, CancellationToken cancellationToken);
}
