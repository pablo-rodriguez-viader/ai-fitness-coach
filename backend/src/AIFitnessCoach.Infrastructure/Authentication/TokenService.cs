using System.Security.Cryptography;
using AIFitnessCoach.Application.Abstractions.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AIFitnessCoach.Infrastructure.Authentication;

public sealed class TokenService : ITokenService
{
    private readonly string issuer;
    private readonly string audience;
    private readonly SymmetricSecurityKey signingKey;
    private readonly int accessTokenMinutes;

    public TokenService(IConfiguration configuration)
    {
        issuer = Required(configuration, "Jwt:Issuer");
        audience = Required(configuration, "Jwt:Audience");
        var encodedKey = Required(configuration, "Jwt:SigningKeyBase64");
        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Jwt:SigningKeyBase64 must be valid Base64.", exception);
        }

        if (keyBytes.Length < 32)
            throw new InvalidOperationException("JWT signing key must contain at least 256 bits.");
        signingKey = new SymmetricSecurityKey(keyBytes);
        accessTokenMinutes = ReadPositiveInt(configuration, "Jwt:AccessTokenMinutes", 15);
    }

    public string CreateAccessToken(string userId, Guid sessionId, DateTimeOffset now)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", userId),
                new System.Security.Claims.Claim("sid", sessionId.ToString("D"))
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(accessTokenMinutes).UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required.");

    private static int ReadPositiveInt(IConfiguration configuration, string key, int defaultValue)
    {
        var configured = configuration[key];
        var value = configured is null ? defaultValue : int.TryParse(configured, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{key} must be an integer.");
        if (value is < 1 or > 1440)
            throw new InvalidOperationException($"{key} must be between 1 and 1440.");
        return value;
    }
}
