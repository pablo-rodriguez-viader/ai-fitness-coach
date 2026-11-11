using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Api.Endpoints;

/// <summary>
/// Apple Health through an iOS Shortcut, for the free personal install: a sideloaded app signed
/// with a free Apple ID has no HealthKit entitlement. The Shortcut posts daily totals with a
/// secret key (Shortcuts:IngestKey); the data goes to the owner's account of the personal sign-in.
/// </summary>
public static class ShortcutEndpoints
{
    public const string KeyHeader = "X-Shortcut-Key";
    public const int MinimumKeyLength = 32;

    public static IEndpointRouteBuilder MapShortcutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/health/shortcut", ImportAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication);
        return endpoints;
    }

    /// <summary>Fails at startup on a key too short to resist guessing.</summary>
    public static void ValidateConfiguration(IConfiguration configuration)
    {
        var key = configuration["Shortcuts:IngestKey"];
        if (!string.IsNullOrWhiteSpace(key) && key.Trim().Length < MinimumKeyLength)
            throw new InvalidOperationException(
                $"Shortcuts:IngestKey must have at least {MinimumKeyLength} characters.");
    }

    private static async Task<IResult> ImportAsync(
        HttpContext context,
        IConfiguration configuration,
        IAuthRepository authRepository,
        IUserConsentStore consentStore,
        SyncHealthMetricsUseCase syncUseCase,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var expectedKey = configuration["Shortcuts:IngestKey"]?.Trim();
        if (string.IsNullOrEmpty(expectedKey))
            return Results.NotFound();
        var sentKey = context.Request.Headers[KeyHeader].ToString().Trim();
        if (!KeysMatch(sentKey, expectedKey))
            return Results.Problem(title: "Invalid Shortcut key.", statusCode: StatusCodes.Status401Unauthorized);

        if (!await authRepository.IdentityExistsAsync(
                IdentityProviders.Personal, IdentityProviders.PersonalOwnerSubject, cancellationToken))
        {
            return Results.Problem(
                title: "Sign in once in the app with your access code before using the Shortcut.",
                statusCode: StatusCodes.Status409Conflict);
        }
        var now = timeProvider.GetUtcNow();
        var userId = await authRepository.GetOrCreatePersonalUserAsync(null, now, cancellationToken);

        if (configuration.GetValue<bool?>("Privacy:RequireHealthConsent") ?? true)
        {
            var consent = await consentStore.GetAsync(userId, ConsentKinds.HealthData, cancellationToken);
            if (consent is not { Granted: true })
            {
                return Results.Problem(
                    title: "Health data consent is required before syncing.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["code"] = "health_consent_required" });
            }
        }

        JsonElement body;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return BadRequest("The body must be JSON.");
        }
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest("The body must be a JSON object.");

        var day = DateOnly.FromDateTime(now.UtcDateTime);
        if (body.TryGetProperty("date", out var dateValue) && dateValue.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(dateValue.GetString()))
        {
            if (!DateOnly.TryParseExact(dateValue.GetString()!.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                return BadRequest("date must look like 2026-11-11.");
            if (day > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1) || day < DateOnly.FromDateTime(now.UtcDateTime).AddDays(-30))
                return BadRequest("date must be within the last 30 days.");
        }

        var values = new ShortcutHealthValues(
            Number(body, "steps"),
            Number(body, "activeEnergy"),
            Number(body, "restingEnergy"),
            Number(body, "heartRate"),
            Number(body, "weight"),
            Number(body, "sleepHours"));
        var metrics = ShortcutHealthImport.ToMetrics(values, day, now);
        if (metrics.Count == 0)
            return BadRequest("No valid values: send at least one of steps, activeEnergy, restingEnergy, heartRate, weight, sleepHours.");

        var result = await syncUseCase.ExecuteAsync(
            userId, new SyncHealthMetricsCommand(metrics), cancellationToken, skipInvalidItems: true);
        return Results.Ok(new
        {
            date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            saved = result.InsertedCount + result.UpdatedCount + result.DuplicateCount,
            metrics = metrics.Select(metric => metric.Metric)
        });
    }

    private static bool KeysMatch(string sent, string expected) =>
        sent.Length > 0
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sent)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    /// <summary>A JSON number, or a string such as "8234" or "71,5" (Shortcuts in Spanish).</summary>
    private static double? Number(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return null;
    }

    private static IResult BadRequest(string message) =>
        Results.Problem(title: message, statusCode: StatusCodes.Status400BadRequest);
}
