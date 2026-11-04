namespace AIFitnessCoach.AI.Models;

public sealed class Message
{
    private Message()
    {
        Content = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public MessageRole Role { get; private set; }
    public string Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Message Create(Guid conversationId, MessageRole role, string content, DateTimeOffset now)
    {
        if (conversationId == Guid.Empty)
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));
        if (string.IsNullOrWhiteSpace(content) || content.Length > 16_000)
            throw new ArgumentException("Message content must contain 1 to 16000 characters.", nameof(content));
        return new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content.Trim(),
            CreatedAt = now.ToUniversalTime()
        };
    }
}

public enum MessageRole
{
    User,
    Assistant
}
