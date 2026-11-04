namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record WorkoutPlanRequest(
    string? Goal,
    int Days = 7,
    int AvailableMinutes = 45,
    IReadOnlyList<string>? Equipment = null);

public sealed record WorkoutFeedbackRequest(
    Guid WorkoutDayId,
    bool Completed,
    string? Difficulty,
    string? Comment);

public sealed record WorkoutCompletionRequest(Guid WorkoutDayId, bool Completed = true);
