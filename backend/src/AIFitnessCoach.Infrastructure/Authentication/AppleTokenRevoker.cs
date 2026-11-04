using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AIFitnessCoach.Application.Abstractions.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AIFitnessCoach.Infrastructure.Authentication;

/// <summary>
/// Uses Apple's REST API: <c>/auth/token</c> turns the fresh authorization code from the app into
/// a refresh token, and <c>/auth/revoke</c> revokes it. The client secret is an ES256 JWT signed
/// with the Sign in with Apple key (Apple:TeamId, Apple:KeyId, Apple:PrivateKey in PEM).
/// </summary>
public sealed class AppleTokenRevoker(
    HttpClient httpClient,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AppleTokenRevoker> logger) : IAppleTokenRevoker
{
    private const string AppleBaseUrl = "https://appleid.apple.com";

    public async Task<AppleRevocationOutcome> RevokeAsync(string? authorizationCode, CancellationToken cancellationToken)
    {
        var clientId = configuration["Apple:Audience"];
        var teamId = configuration["Apple:TeamId"];
        var keyId = configuration["Apple:KeyId"];
        var privateKey = configuration["Apple:PrivateKey"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(teamId)
            || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("Sign in with Apple revocation is not configured; the Apple grant was not revoked.");
            return AppleRevocationOutcome.NotConfigured;
        }
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            logger.LogWarning("Account deleted without an Apple authorization code; the Apple grant was not revoked.");
            return AppleRevocationOutcome.MissingAuthorizationCode;
        }

        try
        {
            var clientSecret = CreateClientSecret(clientId, teamId, keyId, privateKey);
            using var tokenResponse = await httpClient.PostAsync(
                $"{AppleBaseUrl}/auth/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["code"] = authorizationCode,
                    ["grant_type"] = "authorization_code"
                }),
                cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Apple token exchange failed with {StatusCode}.", (int)tokenResponse.StatusCode);
                return AppleRevocationOutcome.Failed;
            }
            var tokens = await tokenResponse.Content.ReadFromJsonAsync<AppleTokenResponse>(cancellationToken);
            var (token, hint) = tokens?.RefreshToken is { Length: > 0 } refresh
                ? (refresh, "refresh_token")
                : (tokens?.AccessToken, "access_token");
            if (string.IsNullOrWhiteSpace(token))
                return AppleRevocationOutcome.Failed;

            using var revokeResponse = await httpClient.PostAsync(
                $"{AppleBaseUrl}/auth/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["token"] = token,
                    ["token_type_hint"] = hint
                }),
                cancellationToken);
            if (!revokeResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Apple token revocation failed with {StatusCode}.", (int)revokeResponse.StatusCode);
                return AppleRevocationOutcome.Failed;
            }
            return AppleRevocationOutcome.Revoked;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or CryptographicException or ArgumentException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            logger.LogWarning(exception, "Apple token revocation failed.");
            return AppleRevocationOutcome.Failed;
        }
    }

    private string CreateClientSecret(string clientId, string teamId, string keyId, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        // Secrets stores often keep the PEM on one line with literal "\n".
        key.ImportFromPem(privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = teamId,
            Audience = AppleBaseUrl,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = clientId },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(key) { KeyId = keyId },
                SecurityAlgorithms.EcdsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed record AppleTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
}
