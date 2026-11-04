namespace AIFitnessCoach.Application.UseCases.Profiles;

public sealed record CreateUserProfileCommand(
    string? DisplayName,
    int Age,
    double HeightCentimeters,
    string? Goal);

public sealed record UserProfileResult(
    string UserId,
    string DisplayName,
    int Age,
    double HeightCentimeters,
    string Goal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
