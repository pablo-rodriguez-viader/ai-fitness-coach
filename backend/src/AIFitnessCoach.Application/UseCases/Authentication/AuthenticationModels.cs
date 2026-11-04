namespace AIFitnessCoach.Application.UseCases.Authentication;

public sealed record AuthenticationTokenPair(
    string UserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

public sealed record AppleSignInCommand(
    string IdentityToken,
    string Nonce,
    string? DisplayName);
