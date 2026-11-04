using System.Text.Json;

namespace AIFitnessCoach.Domain.Entities;

/// <summary>Kinds of messages a beta tester can send from the app.</summary>
public static class SupportReportKinds
{
    public const string Feedback = "feedback";
    public const string Bug = "bug";
    public const string Support = "support";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Feedback, Bug, Support
    };
}

/// <summary>
/// Feedback, bug report or support request sent from the app. The message is free text written by
/// the user for the team; diagnostics only hold app/device metadata and recent event names.
/// Deleted with the account.
/// </summary>
public sealed class SupportReport
{
    public const int MaximumMessageLength = 4000;
    public const int MaximumCategoryLength = 40;
    public const int MaximumEmailLength = 254;
    public const int MaximumDiagnosticsLength = 8000;
    public const int MaximumMetadataLength = 40;

    private SupportReport()
    {
        UserId = string.Empty;
        Kind = string.Empty;
        Category = string.Empty;
        Message = string.Empty;
        Status = "open";
        DiagnosticsJson = "{}";
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public string Kind { get; private set; }
    public string Category { get; private set; }
    public string Message { get; private set; }
    public int? Rating { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? AppVersion { get; private set; }
    public string? BuildNumber { get; private set; }
    public string? OsVersion { get; private set; }
    public string? DeviceModel { get; private set; }
    public string DiagnosticsJson { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static SupportReport Create(
        string userId,
        string kind,
        string? category,
        string message,
        int? rating,
        string? contactEmail,
        string? appVersion,
        string? buildNumber,
        string? osVersion,
        string? deviceModel,
        IReadOnlyDictionary<string, string>? diagnostics,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        if (!SupportReportKinds.All.Contains(kind))
            throw new ArgumentException("Unknown report kind.", nameof(kind));
        var trimmedMessage = message?.Trim() ?? string.Empty;
        if (trimmedMessage.Length is 0 or > MaximumMessageLength)
            throw new ArgumentException("The message is empty or too long.", nameof(message));
        if (rating is < 1 or > 5)
            throw new ArgumentException("Rating must be between 1 and 5.", nameof(rating));
        var email = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        if (email is not null && (email.Length > MaximumEmailLength || !email.Contains('@')))
            throw new ArgumentException("The contact email is invalid.", nameof(contactEmail));
        var diagnosticsJson = JsonSerializer.Serialize(diagnostics ?? new Dictionary<string, string>());
        if (diagnosticsJson.Length > MaximumDiagnosticsLength)
            throw new ArgumentException("Diagnostics are too large.", nameof(diagnostics));

        return new SupportReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            Category = Clip(category, MaximumCategoryLength) ?? "general",
            Message = trimmedMessage,
            Rating = rating,
            ContactEmail = email,
            AppVersion = Clip(appVersion, MaximumMetadataLength),
            BuildNumber = Clip(buildNumber, MaximumMetadataLength),
            OsVersion = Clip(osVersion, MaximumMetadataLength),
            DeviceModel = Clip(deviceModel, MaximumMetadataLength),
            DiagnosticsJson = diagnosticsJson,
            Status = "open",
            CreatedAt = createdAt.ToUniversalTime()
        };
    }

    private static string? Clip(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > maximum ? trimmed[..maximum] : trimmed;
    }
}
