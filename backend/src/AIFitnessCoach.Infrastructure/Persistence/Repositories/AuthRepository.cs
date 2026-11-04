using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class AuthRepository(FitnessDbContext dbContext) : IAuthRepository
{
    public Task<bool> AppleIdentityExistsAsync(string subject, CancellationToken cancellationToken) =>
        dbContext.UserIdentities.AnyAsync(
            item => item.Provider == "apple" && item.ProviderSubject == subject,
            cancellationToken);

    public Task<int> CountUsersAsync(CancellationToken cancellationToken) =>
        dbContext.Users.CountAsync(cancellationToken);

    public Task<string> GetOrCreateAppleUserAsync(
        AppleIdentity identity,
        string? displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(IdentityProviders.Apple, identity, displayName, now, cancellationToken);

    /// <summary>The single owner account of a personal install (Auth:PersonalAccessCode).</summary>
    public Task<string> GetOrCreatePersonalUserAsync(
        string? displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            IdentityProviders.Personal,
            new AppleIdentity(IdentityProviders.PersonalOwnerSubject, null, false),
            displayName,
            now,
            cancellationToken);

    public Task<bool> IdentityExistsAsync(string provider, string subject, CancellationToken cancellationToken) =>
        dbContext.UserIdentities.AnyAsync(
            item => item.Provider == provider && item.ProviderSubject == subject,
            cancellationToken);

    private async Task<string> GetOrCreateAsync(
        string provider,
        AppleIdentity identity,
        string? displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.UserIdentities
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Provider == provider && item.ProviderSubject == identity.Subject,
                cancellationToken);
        if (existing is not null)
            return existing.UserId;

        var user = User.Create(Guid.NewGuid().ToString("D"), displayName, now);
        var userIdentity = UserIdentity.Create(
            Guid.NewGuid(),
            user.Id,
            provider,
            identity.Subject,
            identity.Email,
            identity.EmailVerified,
            now);
        dbContext.Users.Add(user);
        dbContext.UserIdentities.Add(userIdentity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return user.Id;
        }
        catch (DbUpdateException exception)
        {
            dbContext.ChangeTracker.Clear();
            var concurrentIdentity = await dbContext.UserIdentities
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Provider == provider && item.ProviderSubject == identity.Subject,
                    cancellationToken);
            if (concurrentIdentity is not null)
                return concurrentIdentity.UserId;
            throw new InvalidOperationException($"Could not persist the {provider} identity.", exception);
        }
    }

    public async Task CreateSessionAsync(
        AuthSession session,
        RefreshToken refreshToken,
        CancellationToken cancellationToken)
    {
        dbContext.AuthSessions.Add(session);
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshRotationResult?> RotateRefreshTokenAsync(
        string tokenHash,
        Guid replacementId,
        string replacementHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The retrying execution strategy requires explicit transactions to run inside it so the
        // whole unit (lock, rotate, commit) is replayed after a transient failure.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var current = await dbContext.RefreshTokens
                .FromSqlInterpolated(
                    $"SELECT * FROM refresh_tokens WHERE token_hash = {tokenHash} FOR UPDATE")
                .Include(token => token.AuthSession)
                .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
            if (current is null)
                return null;

            if (current.RevokedAt is not null)
            {
                if (current.ReplacedByTokenHash is null)
                    return null;

                var previousReplacementHash = current.ReplacedByTokenHash;
                var previousReplacement = await dbContext.RefreshTokens
                    .FromSqlInterpolated(
                        $"SELECT * FROM refresh_tokens WHERE token_hash = {previousReplacementHash} FOR UPDATE")
                    .FirstOrDefaultAsync(token => token.TokenHash == previousReplacementHash, cancellationToken);
                if (current.AuthSession.IsActive(now)
                    && current.CanRecoverLostRotation(previousReplacement, now))
                {
                    // The client never received (or never stored) the last rotation: hand out a
                    // fresh token and retire the unused one instead of revoking the session.
                    var recovered = RefreshToken.Create(
                        replacementId,
                        current.AuthSessionId,
                        replacementHash,
                        now,
                        replacementExpiresAt);
                    previousReplacement!.Revoke(now, replacementHash);
                    current.Supersede(replacementHash);
                    current.AuthSession.Touch(now);
                    current.AuthSession.ExtendExpiry(replacementExpiresAt);
                    dbContext.RefreshTokens.Add(recovered);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new RefreshRotationResult(current.AuthSession.UserId, current.AuthSessionId);
                }

                current.AuthSession.Revoke(now);
                await RevokeAllSessionTokensAsync(current.AuthSessionId, now, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            if (!current.IsActive(now) || !current.AuthSession.IsActive(now))
                return null;

            var replacement = RefreshToken.Create(
                replacementId,
                current.AuthSessionId,
                replacementHash,
                now,
                replacementExpiresAt);
            current.Revoke(now, replacementHash);
            current.AuthSession.Touch(now);
            current.AuthSession.ExtendExpiry(replacementExpiresAt);
            dbContext.RefreshTokens.Add(replacement);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (RefreshRotationResult?)new RefreshRotationResult(
                current.AuthSession.UserId,
                current.AuthSessionId);
        });
    }

    public async Task RevokeRefreshTokenAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = await dbContext.RefreshTokens
            .Include(item => item.AuthSession)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (token is null)
            return;

        token.AuthSession.Revoke(now);
        await RevokeAllSessionTokensAsync(token.AuthSessionId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(
        string userId,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.AuthSessions
            .FirstOrDefaultAsync(
                item => item.Id == sessionId && item.UserId == userId,
                cancellationToken);
        if (session is null)
            return;

        session.Revoke(now);
        await RevokeAllSessionTokensAsync(sessionId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsSessionActiveAsync(
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.AuthSessions
            .AsNoTracking()
            .AnyAsync(session => session.Id == sessionId
                && session.RevokedAt == null
                && session.ExpiresAt > now,
                cancellationToken);

    public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    private async Task RevokeAllSessionTokensAsync(
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tokens = await dbContext.RefreshTokens
            .Where(item => item.AuthSessionId == sessionId && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.Revoke(now);
    }
}
