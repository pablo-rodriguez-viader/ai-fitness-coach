using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.UseCases.Authentication;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class AuthenticationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthenticationService_AppleSignInCreatesPersistedSessionAndTokens()
    {
        var repository = new FakeAuthRepository();
        var tokens = new FakeTokenService();
        var service = CreateService(repository, tokens);

        var pair = await service.SignInWithAppleAsync(
            new AppleSignInCommand("signed-identity-token", "nonce", "Ada"),
            CancellationToken.None);

        Assert.Equal(repository.UserId, pair.UserId);
        Assert.Equal("access-token", pair.AccessToken);
        Assert.Equal("refresh-token-1", pair.RefreshToken);
        Assert.NotNull(repository.Session);
        Assert.True(await service.IsSessionActiveAsync(
            repository.Session!.Id,
            CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticationService_RotatesRefreshTokenAndRevokesSessionOnLogout()
    {
        var repository = new FakeAuthRepository();
        var service = CreateService(repository, new FakeTokenService());
        var pair = await service.SignInWithAppleAsync(
            new AppleSignInCommand("signed-identity-token", "nonce", null),
            CancellationToken.None);

        var refreshed = await service.RefreshAsync(pair.RefreshToken, CancellationToken.None);

        Assert.Equal("refresh-token-2", refreshed.RefreshToken);
        Assert.NotEqual(pair.RefreshToken, refreshed.RefreshToken);
        await service.RevokeSessionAsync(
            pair.UserId,
            repository.Session!.Id,
            CancellationToken.None);
        Assert.False(await service.IsSessionActiveAsync(
            repository.Session.Id,
            CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticationService_RejectsInvalidAppleIdentityToken()
    {
        var apple = new FakeAppleAuthenticationService
        {
            Error = new UnauthorizedAccessException("Invalid Apple signature.")
        };
        var service = new AuthenticationService(
            new FakeAuthRepository(),
            apple,
            new FakeTokenService(),
            new FixedTimeProvider(Now),
            15,
            30);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SignInWithAppleAsync(
                new AppleSignInCommand("forged-token", "nonce", null),
                CancellationToken.None));
    }

    [Fact]
    public async Task TokenService_IssuesSignedShortLivedJwtWithUserAndSessionClaims()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://issuer.example",
                ["Jwt:Audience"] = "fitness-app",
                ["Jwt:SigningKeyBase64"] = key,
                ["Jwt:AccessTokenMinutes"] = "15"
            })
            .Build();
        var service = new TokenService(configuration);
        var sessionId = Guid.NewGuid();
        var token = service.CreateAccessToken("user-1", sessionId, DateTimeOffset.UtcNow);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "https://issuer.example",
                ValidateAudience = true,
                ValidAudience = "fitness-app",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(key)),
                ClockSkew = TimeSpan.Zero
            });

        Assert.True(validation.IsValid);
        Assert.Equal("user-1", validation.ClaimsIdentity?.FindFirst("sub")?.Value);
        Assert.Equal(sessionId.ToString("D"), validation.ClaimsIdentity?.FindFirst("sid")?.Value);
        Assert.NotEqual(
            service.HashRefreshToken(service.CreateRefreshToken()),
            service.HashRefreshToken(service.CreateRefreshToken()));
    }

    private static AuthenticationService CreateService(
        FakeAuthRepository repository,
        FakeTokenService tokens) =>
        new(
            repository,
            new FakeAppleAuthenticationService(),
            tokens,
            new FixedTimeProvider(Now),
            15,
            30);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAppleAuthenticationService : IAppleAuthenticationService
    {
        public Exception? Error { get; init; }

        public Task<AppleIdentity> ValidateIdentityTokenAsync(
            string identityToken,
            string rawNonce,
            CancellationToken cancellationToken) =>
            Error is null
                ? Task.FromResult(new AppleIdentity("apple-subject", "ada@example.com", true))
                : Task.FromException<AppleIdentity>(Error);
    }

    private sealed class FakeTokenService : ITokenService
    {
        private int tokenCount;

        public string CreateAccessToken(string userId, Guid sessionId, DateTimeOffset now) =>
            "access-token";

        public string CreateRefreshToken() => $"refresh-token-{++tokenCount}";

        public string HashRefreshToken(string token) => $"hash-{token}".PadRight(64, '0')[..64];
    }

    private sealed class FakeAuthRepository : IAuthRepository
    {
        public string UserId { get; } = "f10f7e18-5a34-4e60-9d9d-4ae03ce41947";
        public AuthSession? Session { get; private set; }
        private string? activeRefreshHash;
        private bool sessionRevoked;

        public Task<string> GetOrCreateAppleUserAsync(
            AppleIdentity identity,
            string? displayName,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(UserId);

        public Task CreateSessionAsync(
            AuthSession session,
            RefreshToken refreshToken,
            CancellationToken cancellationToken)
        {
            Session = session;
            activeRefreshHash = refreshToken.TokenHash;
            return Task.CompletedTask;
        }

        public Task<RefreshRotationResult?> RotateRefreshTokenAsync(
            string tokenHash,
            Guid replacementId,
            string replacementHash,
            DateTimeOffset replacementExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (sessionRevoked || tokenHash != activeRefreshHash)
                return Task.FromResult<RefreshRotationResult?>(null);
            activeRefreshHash = replacementHash;
            return Task.FromResult<RefreshRotationResult?>(
                new RefreshRotationResult(UserId, Session!.Id));
        }

        public Task RevokeRefreshTokenAsync(
            string tokenHash,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            sessionRevoked = true;
            return Task.CompletedTask;
        }

        public Task RevokeSessionAsync(
            string userId,
            Guid sessionId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (userId == UserId && Session?.Id == sessionId)
                sessionRevoked = true;
            return Task.CompletedTask;
        }

        public Task<bool> IsSessionActiveAsync(
            Guid sessionId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(!sessionRevoked && Session?.Id == sessionId);

        public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == UserId);
    }
}
