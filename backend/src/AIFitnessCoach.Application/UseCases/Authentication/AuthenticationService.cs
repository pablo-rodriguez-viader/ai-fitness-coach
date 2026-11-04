using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Authentication;

public sealed class AuthenticationService(
    IAuthRepository authRepository,
    IAppleAuthenticationService appleAuthenticationService,
    ITokenService tokenService,
    TimeProvider timeProvider,
    int accessTokenMinutes,
    int refreshTokenDays,
    int? maxUsers = null,
    string? personalAccessCode = null)
{
    /// <summary>Shorter codes are rejected at startup so the personal sign-in cannot be guessed.</summary>
    public const int MinimumPersonalAccessCodeLength = 24;

    public bool IsPersonalSignInEnabled => !string.IsNullOrEmpty(personalAccessCode);

    /// <summary>
    /// Sign-in for a personal install built without Sign in with Apple (free Apple ID). The
    /// code is a secret only the owner knows; it always opens the same single account.
    /// </summary>
    public async Task<AuthenticationTokenPair> SignInWithPersonalCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(personalAccessCode))
            throw new PersonalSignInDisabledException();
        if (string.IsNullOrEmpty(code) || code.Length > 256 || !FixedTimeEquals(code, personalAccessCode))
            throw new UnauthorizedAccessException("The personal access code is invalid.");

        var now = timeProvider.GetUtcNow();
        if (maxUsers is { } cap
            && !await authRepository.IdentityExistsAsync(
                IdentityProviders.Personal, IdentityProviders.PersonalOwnerSubject, cancellationToken)
            && await authRepository.CountUsersAsync(cancellationToken) >= cap)
            throw new BetaFullException(cap);
        var userId = await authRepository.GetOrCreatePersonalUserAsync(null, now, cancellationToken);
        return await CreateSessionAsync(userId, now, cancellationToken);
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(provided));
        var expectedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    public async Task<AuthenticationTokenPair> SignInWithAppleAsync(
        AppleSignInCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdentityToken)
            || command.IdentityToken.Length > 16_384)
            throw new UnauthorizedAccessException("Apple identity token is invalid.");
        if (string.IsNullOrWhiteSpace(command.Nonce) || command.Nonce.Length > 256)
            throw new UnauthorizedAccessException("Apple nonce is invalid.");

        var identity = await appleAuthenticationService.ValidateIdentityTokenAsync(
            command.IdentityToken,
            command.Nonce,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        // Staged beta (10 → 25 → 50): new accounts stop at Beta:MaxUsers; existing testers can
        // always sign in again. Two sign-ups at the same instant may pass the cap by one.
        if (maxUsers is { } cap
            && !await authRepository.AppleIdentityExistsAsync(identity.Subject, cancellationToken)
            && await authRepository.CountUsersAsync(cancellationToken) >= cap)
            throw new BetaFullException(cap);
        var userId = await authRepository.GetOrCreateAppleUserAsync(
            identity,
            command.DisplayName,
            now,
            cancellationToken);
        return await CreateSessionAsync(userId, now, cancellationToken);
    }

    public async Task<AuthenticationTokenPair> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
            throw new UnauthorizedAccessException("Refresh token is invalid.");

        var now = timeProvider.GetUtcNow();
        var replacementToken = tokenService.CreateRefreshToken();
        var rotation = await authRepository.RotateRefreshTokenAsync(
            tokenService.HashRefreshToken(refreshToken),
            Guid.NewGuid(),
            tokenService.HashRefreshToken(replacementToken),
            now.AddDays(refreshTokenDays),
            now,
            cancellationToken);
        if (rotation is null)
            throw new UnauthorizedAccessException("Refresh token is expired, revoked, or invalid.");

        var accessTokenExpiresAt = now.AddMinutes(accessTokenMinutes);
        return new AuthenticationTokenPair(
            rotation.UserId,
            tokenService.CreateAccessToken(rotation.UserId, rotation.SessionId, now),
            replacementToken,
            accessTokenExpiresAt);
    }

    public async Task RevokeAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
            return;
        await authRepository.RevokeRefreshTokenAsync(
            tokenService.HashRefreshToken(refreshToken),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task RevokeSessionAsync(
        string userId,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await authRepository.RevokeSessionAsync(
            userId,
            sessionId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken cancellationToken) =>
        authRepository.IsSessionActiveAsync(
            sessionId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    private async Task<AuthenticationTokenPair> CreateSessionAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var refreshToken = tokenService.CreateRefreshToken();
        var session = AuthSession.Create(sessionId, userId, now, now.AddDays(refreshTokenDays));
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            sessionId,
            tokenService.HashRefreshToken(refreshToken),
            now,
            now.AddDays(refreshTokenDays));
        await authRepository.CreateSessionAsync(session, token, cancellationToken);

        return new AuthenticationTokenPair(
            userId,
            tokenService.CreateAccessToken(userId, sessionId, now),
            refreshToken,
            now.AddMinutes(accessTokenMinutes));
    }
}

/// <summary>The private beta has reached Beta:MaxUsers; the API answers 403 with code "beta_full".</summary>
public sealed class BetaFullException(int maxUsers)
    : Exception($"The private beta is full ({maxUsers} users).")
{
    public int MaxUsers { get; } = maxUsers;
}

/// <summary>Auth:PersonalAccessCode is not configured: the personal sign-in does not exist (404).</summary>
public sealed class PersonalSignInDisabledException() : Exception("Personal sign-in is not enabled.");
