namespace AIFitnessCoach.AI.Models;

public sealed class AIContextSnapshot
{
    private AIContextSnapshot()
    {
        UserId = string.Empty;
        Json = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public string UserId { get; private set; }
    public int ContextVersion { get; private set; }
    public string Json { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AIContextSnapshot Create(
        Guid conversationId,
        string userId,
        int contextVersion,
        string json,
        DateTimeOffset createdAt)
    {
        if (conversationId == Guid.Empty || string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid conversation and user are required.");
        if (contextVersion < 1 || string.IsNullOrWhiteSpace(json) || json.Length > 100_000)
            throw new ArgumentException("A valid context snapshot is required.", nameof(json));
        return new AIContextSnapshot
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            UserId = userId,
            ContextVersion = contextVersion,
            Json = json,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }
}
