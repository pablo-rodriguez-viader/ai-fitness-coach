using System.Security.Cryptography;
using System.Text;

namespace AIFitnessCoach.Api.Configuration;

/// <summary>
/// Shared secret for the team's beta endpoints (product report, support inbox). Without a
/// configured key those endpoints answer 404 instead of being open.
/// </summary>
public static class AdminApiKey
{
    public const string Header = "X-Analytics-Key";
    public const string ConfigurationKey = "Analytics:AdminApiKey";
    public const int MinimumLength = 32;

    /// <summary>Null when the request may proceed; otherwise the response to return.</summary>
    public static IResult? Check(HttpContext httpContext, IConfiguration configuration)
    {
        var configuredKey = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < MinimumLength)
            return Results.NotFound();
        var providedKey = httpContext.Request.Headers[Header].ToString();
        return FixedTimeEquals(providedKey, configuredKey) ? null : Results.Unauthorized();
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
