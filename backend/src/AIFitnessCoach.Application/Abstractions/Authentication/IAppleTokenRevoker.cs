namespace AIFitnessCoach.Application.Abstractions.Authentication;

public enum AppleRevocationOutcome
{
    Revoked,
    /// <summary>Apple:TeamId, Apple:KeyId or Apple:PrivateKey are not set (allowed for the private beta only).</summary>
    NotConfigured,
    /// <summary>The app did not send an authorization code.</summary>
    MissingAuthorizationCode,
    Failed
}

/// <summary>
/// Revokes the user's Sign in with Apple grant when the account is deleted, as App Store Review
/// Guideline 5.1.1(v) requires. Exchanges a fresh authorization code for a token and revokes it.
/// </summary>
public interface IAppleTokenRevoker
{
    Task<AppleRevocationOutcome> RevokeAsync(string? authorizationCode, CancellationToken cancellationToken);
}
