namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record AuthenticationResponse(
    string UserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);
