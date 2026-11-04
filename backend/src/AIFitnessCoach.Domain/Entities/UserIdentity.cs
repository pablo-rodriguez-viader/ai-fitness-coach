namespace AIFitnessCoach.Domain.Entities;

public static class IdentityProviders
{
    public const string Apple = "apple";

    /// <summary>
    /// Personal install without Sign in with Apple (free Apple ID): one owner account, reached
    /// with the secret Auth:PersonalAccessCode. Off unless that code is configured.
    /// </summary>
    public const string Personal = "personal";
    public const string PersonalOwnerSubject = "owner";
}

public sealed class UserIdentity
{
    private UserIdentity()
    {
        Provider = string.Empty;
        ProviderSubject = string.Empty;
        UserId = string.Empty;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public string Provider { get; private set; }
    public string ProviderSubject { get; private set; }
    public string? Email { get; private set; }
    public bool EmailVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public User User { get; private set; } = null!;

    public static UserIdentity Create(
        Guid id,
        string userId,
        string provider,
        string providerSubject,
        string? email,
        bool emailVerified,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid identity and user id are required.");
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 32)
            throw new ArgumentException("A valid identity provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(providerSubject) || providerSubject.Length > 256)
            throw new ArgumentException("A valid provider subject is required.", nameof(providerSubject));
        if (email?.Length > 320)
            throw new ArgumentException("Email is too long.", nameof(email));

        return new UserIdentity
        {
            Id = id,
            UserId = userId,
            Provider = provider,
            ProviderSubject = providerSubject,
            Email = email,
            EmailVerified = emailVerified,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }
}
