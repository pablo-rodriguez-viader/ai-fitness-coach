namespace AIFitnessCoach.Domain.Entities;

/// <summary>Consents recorded as proof under GDPR art. 7(1). AI consent keeps its own table.</summary>
public static class ConsentKinds
{
    /// <summary>Processing of synced Apple Health data on our servers (GDPR art. 9(2)(a)).</summary>
    public const string HealthData = "health_data";

    /// <summary>Usage analytics and diagnostics sent by the app (crashes, errors, screens).</summary>
    public const string Analytics = "analytics";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        HealthData, Analytics
    };
}

/// <summary>The latest decision of a user for one consent kind, with the text version shown.</summary>
public sealed class UserConsent
{
    private UserConsent()
    {
        UserId = string.Empty;
        Kind = string.Empty;
    }

    public string UserId { get; private set; }
    public string Kind { get; private set; }
    public bool Granted { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserConsent Create(string userId, string kind, bool granted, int version, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        if (!ConsentKinds.All.Contains(kind))
            throw new ArgumentException("Unknown consent kind.", nameof(kind));
        if (version is < 1 or > 1000)
            throw new ArgumentException("Invalid consent text version.", nameof(version));
        return new UserConsent
        {
            UserId = userId,
            Kind = kind,
            Granted = granted,
            Version = version,
            UpdatedAt = now.ToUniversalTime()
        };
    }

    public void Update(bool granted, int version, DateTimeOffset now)
    {
        if (version is < 1 or > 1000)
            throw new ArgumentException("Invalid consent text version.", nameof(version));
        Granted = granted;
        Version = version;
        UpdatedAt = now.ToUniversalTime();
    }
}
