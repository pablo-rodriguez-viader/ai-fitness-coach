namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record AppleSignInRequest(
    string? IdentityToken,
    string? Nonce,
    string? DisplayName);
