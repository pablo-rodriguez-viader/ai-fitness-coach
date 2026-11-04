namespace AIFitnessCoach.Domain.Entities;

public sealed class AuthSession
{
    private AuthSession()
    {
        UserId = string.Empty;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public User User { get; private set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    public static AuthSession Create(
        Guid id,
        string userId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A session id and user id are required.");
        if (expiresAt <= createdAt)
            throw new ArgumentException("Session expiry must be after creation.", nameof(expiresAt));

        return new AuthSession
        {
            Id = id,
            UserId = userId,
            CreatedAt = createdAt.ToUniversalTime(),
            LastSeenAt = createdAt.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime()
        };
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now.ToUniversalTime();
    }

    public void Touch(DateTimeOffset now)
    {
        if (now > LastSeenAt)
            LastSeenAt = now.ToUniversalTime();
    }

    public void ExtendExpiry(DateTimeOffset expiresAt)
    {
        if (RevokedAt is null && expiresAt > ExpiresAt)
            ExpiresAt = expiresAt.ToUniversalTime();
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
