namespace AIFitnessCoach.Domain.Entities;

public sealed class RefreshToken
{
    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid AuthSessionId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public AuthSession AuthSession { get; private set; } = null!;

    public static RefreshToken Create(
        Guid id,
        Guid authSessionId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || authSessionId == Guid.Empty)
            throw new ArgumentException("A token and session id are required.");
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
            throw new ArgumentException("A SHA-256 token hash is required.", nameof(tokenHash));
        if (expiresAt <= createdAt)
            throw new ArgumentException("Token expiry must be after creation.", nameof(expiresAt));

        return new RefreshToken
        {
            Id = id,
            AuthSessionId = authSessionId,
            TokenHash = tokenHash,
            CreatedAt = createdAt.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime()
        };
    }

    public void Revoke(DateTimeOffset now, string? replacementHash = null)
    {
        RevokedAt ??= now.ToUniversalTime();
        ReplacedByTokenHash = replacementHash;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>
    /// How long after a rotation the previous token may be presented again without being
    /// treated as theft. Covers a mobile client that never received the rotation response.
    /// </summary>
    public static readonly TimeSpan LostRotationRecoveryWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// True when this token was rotated recently and its replacement was never used, which is
    /// what a lost rotation response looks like. Any other reuse must revoke the session.
    /// </summary>
    public bool CanRecoverLostRotation(RefreshToken? replacement, DateTimeOffset now) =>
        RevokedAt is { } revokedAt
        && ReplacedByTokenHash is not null
        && replacement is not null
        && replacement.AuthSessionId == AuthSessionId
        && string.Equals(replacement.TokenHash, ReplacedByTokenHash, StringComparison.Ordinal)
        && replacement.IsActive(now)
        && now - revokedAt <= LostRotationRecoveryWindow;

    /// <summary>Points this already-rotated token at a new replacement, keeping its original revocation time.</summary>
    public void Supersede(string replacementHash)
    {
        if (RevokedAt is null)
            throw new InvalidOperationException("Only a rotated token can be superseded.");
        ReplacedByTokenHash = replacementHash;
    }
}
