using System.Security.Cryptography;
using System.Text;
using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AIFitnessCoach.Infrastructure.Authentication;

public sealed class AppleAuthenticationService : IAppleAuthenticationService
{
    private const string AppleIssuer = "https://appleid.apple.com";
    private static readonly HttpDocumentRetriever DocumentRetriever = new() { RequireHttps = true };
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> AppleConfiguration =
        new(
            $"{AppleIssuer}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            DocumentRetriever);

    private readonly string audience;

    public AppleAuthenticationService(IConfiguration configuration)
    {
        audience = string.IsNullOrWhiteSpace(configuration["Apple:Audience"])
            ? throw new InvalidOperationException("Apple:Audience must be configured with the app's bundle id.")
            : configuration["Apple:Audience"]!;
    }

    public async Task<AppleIdentity> ValidateIdentityTokenAsync(
        string identityToken,
        string rawNonce,
        CancellationToken cancellationToken)
    {
        var appleConfiguration = await AppleConfiguration.GetConfigurationAsync(cancellationToken);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = AppleIssuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = appleConfiguration.SigningKeys,
            ClockSkew = TimeSpan.FromSeconds(60)
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(identityToken, parameters);
        if (!result.IsValid)
            throw new UnauthorizedAccessException("Apple identity token is invalid.");

        var subject = result.ClaimsIdentity?.FindFirst("sub")?.Value;
        var tokenNonce = result.ClaimsIdentity?.FindFirst("nonce")?.Value;
        var email = result.ClaimsIdentity?.FindFirst("email")?.Value;
        var emailVerified = string.Equals(
            result.ClaimsIdentity?.FindFirst("email_verified")?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > 256
            || string.IsNullOrWhiteSpace(tokenNonce)
            || !NonceMatches(rawNonce, tokenNonce))
            throw new UnauthorizedAccessException("Apple identity token claims are invalid.");

        return new AppleIdentity(subject, email, emailVerified);
    }

    private static bool NonceMatches(string rawNonce, string tokenNonce)
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce)));
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(tokenNonce));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
