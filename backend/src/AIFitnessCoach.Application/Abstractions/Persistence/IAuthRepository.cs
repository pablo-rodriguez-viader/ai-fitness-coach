using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.Abstractions.Persistence;

public interface IAuthRepository
{
    /// <summary>True when this Apple ID already has an account (never blocked by the beta cap).</summary>
    Task<bool> AppleIdentityExistsAsync(string subject, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>Registered accounts, compared with Beta:MaxUsers.</summary>
    Task<int> CountUsersAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    Task<bool> IdentityExistsAsync(string provider, string subject, CancellationToken cancellationToken) =>
        provider == IdentityProviders.Apple
            ? AppleIdentityExistsAsync(subject, cancellationToken)
            : Task.FromResult(true);

    /// <summary>The single owner account of a personal install (see IdentityProviders.Personal).</summary>
    Task<string> GetOrCreatePersonalUserAsync(
        string? displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Personal sign-in is not supported by this repository.");

    Task<string> GetOrCreateAppleUserAsync(
        AppleIdentity identity,
        string? displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CreateSessionAsync(
        AuthSession session,
        RefreshToken refreshToken,
        CancellationToken cancellationToken);
    Task<RefreshRotationResult?> RotateRefreshTokenAsync(
        string tokenHash,
        Guid replacementId,
        string replacementHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task RevokeRefreshTokenAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task RevokeSessionAsync(
        string userId,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> IsSessionActiveAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken);
}

public sealed record RefreshRotationResult(string UserId, Guid SessionId);

public sealed record AppleIdentity(
    string Subject,
    string? Email,
    bool EmailVerified);
