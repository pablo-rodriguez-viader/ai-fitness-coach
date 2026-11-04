namespace AIFitnessCoach.Domain.Entities;

public sealed class User
{
    private User()
    {
        Id = string.Empty;
        DisplayName = string.Empty;
    }

    public string Id { get; private set; }
    public string DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ICollection<UserIdentity> Identities { get; private set; } = new List<UserIdentity>();
    public ICollection<AuthSession> Sessions { get; private set; } = new List<AuthSession>();
    public ICollection<HealthMetric> HealthMetrics { get; private set; } = new List<HealthMetric>();
    public UserProfile? Profile { get; private set; }

    public static User Create(string id, string? displayName, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(id));

        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? "AI Fitness Coach"
            : displayName.Trim();
        if (normalizedDisplayName.Length > 120)
            normalizedDisplayName = normalizedDisplayName[..120];

        return new User
        {
            Id = id,
            DisplayName = normalizedDisplayName,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }
}
