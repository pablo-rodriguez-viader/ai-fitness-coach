using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Contracts;

public sealed record WorkoutPlanRequest(
    string Goal,
    int Days = 7,
    int AvailableMinutes = 45,
    IReadOnlyList<string>? Equipment = null,
    string? Needs = null);

public sealed record WorkoutFeedbackRequest(
    Guid WorkoutDayId,
    bool Completed,
    string Difficulty,
    string? Comment);

public sealed record WorkoutPlanDocument(string Goal, IReadOnlyList<WorkoutDayDocument> Days);

public sealed record WorkoutDayDocument(
    string Title,
    string? RecoveryNote,
    IReadOnlyList<WorkoutExerciseDocument> Exercises);

public sealed record WorkoutExerciseDocument(
    string Name,
    string? Sets,
    string? Repetitions,
    int? DurationMinutes,
    int? RestSeconds,
    string? Instructions)
{
    public string ExerciseId { get; init; } = string.Empty;
}

public sealed record WorkoutPlanView(
    Guid Id,
    string Goal,
    DateOnly StartsOn,
    DateOnly EndsOn,
    DateTimeOffset CreatedAt,
    IReadOnlyList<WorkoutDayView> Days);

public sealed record WorkoutDayView(
    Guid Id,
    DateOnly Date,
    string Title,
    string RecoveryNote,
    IReadOnlyList<WorkoutExerciseView> Exercises);

public sealed record WorkoutExerciseView(
    Guid Id,
    int Position,
    string Name,
    string? Sets,
    string? Repetitions,
    int? DurationMinutes,
    int? RestSeconds,
    string? Instructions);

public sealed record WorkoutRecommendationView(string Type, string Text, DateTimeOffset CreatedAt);

public sealed record WorkoutFeedbackResult(Guid Id, bool Completed, string Difficulty, string Adaptation);

public static class WorkoutGoal
{
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fat_loss",
        "muscle_gain",
        "maintenance",
        "cardiovascular_endurance",
        "running",
        "general_health"
    };
}
