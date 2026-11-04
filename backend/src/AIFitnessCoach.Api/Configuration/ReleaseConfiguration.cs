using AIFitnessCoach.AI.Configuration;
using Microsoft.Extensions.Options;

namespace AIFitnessCoach.Api.Configuration;

/// <summary>
/// Remote configuration the app reads at launch: environment, support contacts, legal links,
/// minimum supported build and feature flags. Flags are kill switches for existing screens so a
/// broken flow can be hidden from testers without a new TestFlight build.
/// </summary>
public sealed record AppConfigResponse(
    string Environment,
    int MinimumSupportedBuild,
    string? SupportEmail,
    string? PrivacyPolicyUrl,
    string? TermsUrl,
    IReadOnlyDictionary<string, bool> Features);

public static class FeatureFlags
{
    public const string Coach = "coach";
    public const string ActivityAnalysis = "activity_analysis";
    public const string Workouts = "workouts";
    public const string Nutrition = "nutrition";
    public const string Goals = "goals";
    public const string Progress = "progress";
    public const string Engagement = "engagement";
    public const string FeedbackCenter = "feedback_center";
    public const string DataExport = "data_export";
    public const string BetaFeedback = "beta_feedback";

    /// <summary>Every flag defaults to on; configuration under FeatureFlags:* turns one off.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Coach, ActivityAnalysis, Workouts, Nutrition, Goals, Progress, Engagement, FeedbackCenter, DataExport,
        BetaFeedback
    ];

    /// <summary>Flows that call the AI provider and cannot work while AI:Enabled is false.</summary>
    public static readonly IReadOnlySet<string> RequireAI = new HashSet<string>(StringComparer.Ordinal)
    {
        Coach, ActivityAnalysis, Workouts
    };
}

public static class ReleaseConfiguration
{
    public static AppConfigResponse Build(IConfiguration configuration, IOptions<AIOptions> aiOptions)
    {
        var aiEnabled = aiOptions.Value.Enabled;
        var features = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var flag in FeatureFlags.All)
        {
            var enabled = configuration.GetValue<bool?>($"FeatureFlags:{flag}") ?? true;
            features[flag] = enabled && (aiEnabled || !FeatureFlags.RequireAI.Contains(flag));
        }
        return new AppConfigResponse(
            Environment: NonEmpty(configuration["Release:Environment"]) ?? "beta",
            MinimumSupportedBuild: Math.Max(0, configuration.GetValue<int?>("Release:MinimumSupportedBuild") ?? 0),
            SupportEmail: NonEmpty(configuration["Release:SupportEmail"]),
            PrivacyPolicyUrl: HttpsUrl(configuration["Release:PrivacyPolicyUrl"]),
            TermsUrl: HttpsUrl(configuration["Release:TermsUrl"]),
            Features: features);
    }

    /// <summary>Fails at startup on values the app would reject, instead of during the beta.</summary>
    public static void Validate(IConfiguration configuration)
    {
        foreach (var key in new[] { "Release:PrivacyPolicyUrl", "Release:TermsUrl" })
        {
            if (NonEmpty(configuration[key]) is { } value && HttpsUrl(value) is null)
                throw new InvalidOperationException($"{key} must be an absolute HTTPS URL.");
        }
        if (NonEmpty(configuration["Release:SupportEmail"]) is { } email && !email.Contains('@'))
            throw new InvalidOperationException("Release:SupportEmail is not a valid email address.");
        var unknown = configuration.GetSection("FeatureFlags").GetChildren()
            .Select(child => child.Key)
            .Where(key => !FeatureFlags.All.Contains(key))
            .ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Unknown feature flags: {string.Join(", ", unknown)}.");
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? HttpsUrl(string? value) =>
        NonEmpty(value) is { } trimmed
        && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : null;
}
