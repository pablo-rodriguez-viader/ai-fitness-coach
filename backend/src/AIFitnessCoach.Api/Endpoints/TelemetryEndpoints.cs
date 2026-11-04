using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Endpoints;

public sealed record ClientTelemetryEvent(
    string? Name,
    DateTimeOffset? OccurredAt,
    IReadOnlyDictionary<string, string>? Properties);

public sealed record ClientTelemetryBatch(
    IReadOnlyList<ClientTelemetryEvent>? Events,
    string? AppVersion,
    string? BuildNumber,
    string? OsVersion,
    string? DeviceModel,
    string? Environment = null);

public sealed record ClientTelemetryResult(int Accepted, int Dropped);

/// <summary>
/// Receives crash, error, usage and sync events from the app. Events are written as structured
/// logs and counted in meters; nothing is stored in the database and no health values are accepted.
/// </summary>
public static partial class TelemetryEndpoints
{
    public const int MaximumEvents = 50;
    public const int MaximumProperties = 12;
    public const int MaximumValueLength = 120;

    // Only these names become metric tags, so a client cannot create unbounded series.
    public static readonly IReadOnlySet<string> KnownEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "app_launch", "app_foreground", "app_background", "screen_view",
        "crash_report", "hang_report", "error",
        "sync_completed", "sync_failed", "sync_dead_lettered", "sync_background_refresh",
        "session_restored", "session_expired", "sign_in", "sign_out",
        "coach_message_sent", "workout_plan_generated", "workout_feedback_sent", "meal_logged", "goal_created",
        "health_connected", "deep_link_opened",
        "session_start", "workout_started", "notification_opened", "notifications_permission",
        "healthkit_authorization", "healthkit_observer_update", "healthkit_query_failed",
        "ai_request", "ai_consent_changed",
        "support_report_sent", "data_export_requested", "account_deleted",
        "app_config_loaded", "app_config_failed", "screen_unavailable",
        "healthkit_data_check", "beta_feedback_prompt_shown", "beta_feedback_prompt_dismissed",
        "consent_changed", "data_deleted"
    };

    private static readonly Meter Meter = new("AIFitnessCoach.Client");
    private static readonly Counter<long> Events = Meter.CreateCounter<long>("client_events_total");
    private static readonly Counter<long> Crashes = Meter.CreateCounter<long>("client_crashes_total");
    private static readonly Counter<long> DroppedEvents = Meter.CreateCounter<long>("client_events_dropped_total");

    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/telemetry/events", ReceiveEvents)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Telemetry);
        return endpoints;
    }

    private static async Task<IResult> ReceiveEvents(
        ClientTelemetryBatch? batch,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        [FromServices] IProductAnalyticsRepository analytics,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (batch?.Events is not { Count: > 0 } events)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["events"] = ["At least one event is required."]
            });
        if (events.Count > MaximumEvents)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["events"] = [$"A batch cannot contain more than {MaximumEvents} events."]
            });

        var userId = currentUser.GetRequiredUserId(principal);
        var logger = loggerFactory.CreateLogger("ClientTelemetry");
        var appVersion = Clean(batch.AppVersion) ?? "unknown";
        var build = Clean(batch.BuildNumber) ?? "unknown";
        var osVersion = Clean(batch.OsVersion) ?? "unknown";
        var deviceModel = Clean(batch.DeviceModel) ?? "unknown";
        var environment = Clean(batch.Environment) ?? "unknown";
        var accepted = 0;
        var dropped = 0;
        var receivedAt = timeProvider.GetUtcNow();
        var stored = new List<ProductEvent>();

        foreach (var item in events)
        {
            if (!TryNormalize(item, out var name, out var properties))
            {
                dropped++;
                continue;
            }
            accepted++;
            if (ProductEventNames.StoredClientEvents.Contains(name))
            {
                stored.Add(ProductEvent.Create(
                    userId,
                    name,
                    item.OccurredAt ?? receivedAt,
                    receivedAt,
                    properties,
                    appVersion));
            }
            var metricName = KnownEvents.Contains(name) ? name : "other";
            Events.Add(1,
                new KeyValuePair<string, object?>("name", metricName),
                new KeyValuePair<string, object?>("app_version", appVersion));
            if (name == "crash_report")
            {
                var count = properties.TryGetValue("count", out var rawCount)
                    && int.TryParse(rawCount, out var parsed) && parsed is > 0 and < 1_000
                        ? parsed
                        : 1;
                Crashes.Add(count, new KeyValuePair<string, object?>("app_version", appVersion));
            }

            var level = name is "crash_report" or "hang_report" or "error" or "sync_failed" or "sync_dead_lettered"
                ? LogLevel.Warning
                : LogLevel.Information;
            logger.Log(
                level,
                "Client event {EventName} at {OccurredAt} from user {UserId} app {AppVersion} ({Build}, {AppEnvironment}) iOS {OsVersion} {DeviceModel}: {Properties}",
                name,
                item.OccurredAt ?? DateTimeOffset.UtcNow,
                userId,
                appVersion,
                build,
                environment,
                osVersion,
                deviceModel,
                properties);
        }

        if (dropped > 0)
            DroppedEvents.Add(dropped);
        try
        {
            await analytics.AddEventsAsync(stored, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logs above keep the events; a storage failure must not make the app resend them.
            logger.LogWarning(exception, "Product events could not be stored ({Count}).", stored.Count);
        }
        return Results.Accepted(value: new ClientTelemetryResult(accepted, dropped));
    }

    public static bool TryNormalize(
        ClientTelemetryEvent item,
        out string name,
        out Dictionary<string, string> properties)
    {
        name = item.Name?.Trim() ?? string.Empty;
        properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!EventNamePattern().IsMatch(name))
            return false;
        if (item.Properties is { } source)
        {
            if (source.Count > MaximumProperties)
                return false;
            foreach (var (key, value) in source)
            {
                if (!PropertyNamePattern().IsMatch(key) || value is null)
                    return false;
                var trimmed = value.Trim();
                properties[key] = trimmed.Length > MaximumValueLength
                    ? trimmed[..MaximumValueLength]
                    : trimmed;
            }
        }
        return true;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > 40 ? trimmed[..40] : trimmed;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,47}$")]
    private static partial Regex EventNamePattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$")]
    private static partial Regex PropertyNamePattern();
}
