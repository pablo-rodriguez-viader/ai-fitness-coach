namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record UserProfileRequest(
    string? DisplayName,
    int Age,
    double HeightCentimeters,
    string? Goal);
