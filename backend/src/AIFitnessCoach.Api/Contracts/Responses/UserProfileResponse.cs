namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record UserProfileResponse(
    string UserId,
    string DisplayName,
    int Age,
    double HeightCentimeters,
    string Goal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
