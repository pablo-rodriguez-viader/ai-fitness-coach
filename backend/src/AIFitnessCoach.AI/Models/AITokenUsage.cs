namespace AIFitnessCoach.AI.Models;

public sealed class AITokenUsage
{
    private AITokenUsage()
    {
        UserId = string.Empty;
        Model = string.Empty;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public Guid ConversationId { get; private set; }
    public string Model { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal EstimatedCost { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AITokenUsage Create(
        string userId,
        Guid conversationId,
        string model,
        int inputTokens,
        int outputTokens,
        decimal estimatedCost,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        if (conversationId == Guid.Empty || string.IsNullOrWhiteSpace(model) || model.Length > 128)
            throw new ArgumentException("A valid conversation and model are required.");
        if (inputTokens < 0 || outputTokens < 0 || estimatedCost < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens), "Token usage and cost must be non-negative.");
        return new AITokenUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConversationId = conversationId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCost = estimatedCost,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }
}
