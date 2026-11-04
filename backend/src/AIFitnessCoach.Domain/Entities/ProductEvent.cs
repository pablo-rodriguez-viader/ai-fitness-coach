using System.Text.Json;

namespace AIFitnessCoach.Domain.Entities;

/// <summary>Names of the events the product analytics are computed from.</summary>
public static class ProductEventNames
{
    // Sent by the app.
    public const string SessionStart = "session_start";
    public const string ScreenView = "screen_view";
    public const string WorkoutStarted = "workout_started";
    public const string NotificationOpened = "notification_opened";
    public const string HealthConnected = "health_connected";
    public const string SyncCompleted = "sync_completed";
    public const string SyncFailed = "sync_failed";
    public const string SyncDeadLettered = "sync_dead_lettered";
    public const string HealthKitAuthorization = "healthkit_authorization";
    /// <summary>After the Health sheet: how many requested data types returned any sample.</summary>
    public const string HealthKitDataCheck = "healthkit_data_check";
    public const string CrashReport = "crash_report";
    public const string HangReport = "hang_report";
    public const string Error = "error";

    // Recorded by the API when the action succeeds, so they cannot be lost or faked by the client.
    public const string WorkoutPlanGenerated = "workout_plan_generated";
    public const string WorkoutCompleted = "workout_completed";
    public const string WorkoutFeedbackSubmitted = "workout_feedback_submitted";
    public const string CoachQuery = "coach_query";
    public const string GoalCreated = "goal_created";
    public const string GoalCompleted = "goal_completed";
    public const string MealLogged = "meal_logged";
    public const string CoachFeedback = "coach_feedback";

    /// <summary>Client events that are stored; everything else from the app only goes to logs.
    /// Sync, HealthKit, crash and error events are stored so the beta report shows stability
    /// without a log aggregator. None of them carries health values or user text.</summary>
    public static readonly IReadOnlySet<string> StoredClientEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionStart, ScreenView, WorkoutStarted, NotificationOpened, HealthConnected,
        SyncCompleted, SyncFailed, SyncDeadLettered, HealthKitAuthorization, HealthKitDataCheck,
        CrashReport, HangReport, Error
    };

    /// <summary>Events that do not make a user "active": the app produces them on its own
    /// (background sync, crash summaries delivered at launch, errors).</summary>
    public static readonly IReadOnlySet<string> PassiveEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        SyncCompleted, SyncFailed, SyncDeadLettered, CrashReport, HangReport, Error
    };

    /// <summary>
    /// Short, non-reversible fingerprint of a question so the report can count repeated
    /// questions without storing their text in analytics. Case, accents' spacing and
    /// punctuation at the ends do not change it.
    /// </summary>
    public static string QuestionHash(string question)
    {
        var normalized = string.Join(' ', question.Trim().TrimEnd('?', '!', '.', '¿', '¡').TrimStart('¿', '¡')
                .ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>Actions that show the user made progress; they count for streaks and goal adherence.</summary>
    public static readonly IReadOnlySet<string> ProgressActions = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkoutCompleted, WorkoutFeedbackSubmitted, MealLogged, CoachQuery, GoalCreated
    };

    /// <summary>Deliberate interactions with the product (not passive views).</summary>
    public static readonly IReadOnlySet<string> Interactions = new HashSet<string>(StringComparer.Ordinal)
    {
        CoachQuery, WorkoutFeedbackSubmitted, MealLogged, GoalCreated, WorkoutStarted, WorkoutPlanGenerated
    };
}

public sealed class ProductEvent
{
    public const int MaximumProperties = 12;

    private ProductEvent()
    {
        UserId = string.Empty;
        Name = string.Empty;
        PropertiesJson = "{}";
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? AppVersion { get; private set; }
    /// <summary>Small string map stored as JSON text; never contains health values.</summary>
    public string PropertiesJson { get; private set; }

    public IReadOnlyDictionary<string, string> Properties =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(PropertiesJson)
        ?? new Dictionary<string, string>();

    public static ProductEvent Create(
        string userId,
        string name,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string>? properties = null,
        string? appVersion = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 48)
            throw new ArgumentException("A valid event name is required.", nameof(name));
        if (properties is { Count: > MaximumProperties })
            throw new ArgumentException("Too many event properties.", nameof(properties));
        // Clock-skewed or replayed client events are pinned to the time the server received them.
        var occurred = occurredAt > receivedAt.AddMinutes(5) || occurredAt < receivedAt.AddDays(-35)
            ? receivedAt
            : occurredAt;
        return new ProductEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            OccurredAt = occurred.ToUniversalTime(),
            ReceivedAt = receivedAt.ToUniversalTime(),
            AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim()[..Math.Min(appVersion.Trim().Length, 40)],
            PropertiesJson = JsonSerializer.Serialize(properties ?? new Dictionary<string, string>())
        };
    }
}
