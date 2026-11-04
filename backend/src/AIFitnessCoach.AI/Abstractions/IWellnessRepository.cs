using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Contracts;

namespace AIFitnessCoach.AI.Abstractions;

public interface IWellnessRepository
{
    Task<NutritionUserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken);
    Task<NutritionProfile?> GetNutritionProfileAsync(string userId, CancellationToken cancellationToken);
    Task SaveNutritionProfileAsync(NutritionProfile profile, CancellationToken cancellationToken);
    Task<Goal?> GetActiveGoalAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Goal>> GetGoalsAsync(string userId, CancellationToken cancellationToken);
    Task AddGoalAsync(Goal goal, CancellationToken cancellationToken);
    Task PauseActiveGoalsAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveMacroTargetAsync(MacroTarget target, CancellationToken cancellationToken);
    Task<MacroTarget?> GetMacroTargetAsync(string userId, DateOnly date, CancellationToken cancellationToken);
    Task AddNutritionLogAsync(NutritionLog log, CancellationToken cancellationToken);
    Task<IReadOnlyList<NutritionLog>> GetNutritionLogsAsync(
        string userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyNutritionSummary>> GetNutritionSummariesAsync(
        string userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AIHealthMetricContext>> GetWeightMetricsAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AIHealthMetricContext>> GetProgressMetricsAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken);
    Task SaveGoalProgressAsync(GoalProgress progress, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoalProgress>> GetGoalProgressAsync(Guid goalId, CancellationToken cancellationToken);
}
