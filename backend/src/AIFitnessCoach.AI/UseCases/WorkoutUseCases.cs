using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Services;

namespace AIFitnessCoach.AI.UseCases;

public sealed class GenerateWorkoutPlanUseCase(WorkoutService service)
{
    public Task<WorkoutPlanView> ExecuteAsync(
        string userId,
        WorkoutPlanRequest request,
        CancellationToken cancellationToken) =>
        service.GeneratePlanAsync(
            userId,
            request.Goal,
            request.Days,
            cancellationToken,
            request.AvailableMinutes,
            request.Equipment);
}

public sealed class GenerateDailyWorkoutUseCase(WorkoutService service)
{
    public Task<WorkoutPlanView> ExecuteAsync(
        string userId,
        string goal,
        CancellationToken cancellationToken) =>
        service.GeneratePlanAsync(userId, goal, 1, cancellationToken);
}

public sealed class AdaptWorkoutPlanUseCase(WorkoutService service)
{
    public Task<WorkoutFeedbackResult> ExecuteAsync(
        string userId,
        WorkoutFeedbackRequest request,
        CancellationToken cancellationToken) =>
        service.RecordFeedbackAsync(userId, request, cancellationToken);
}
