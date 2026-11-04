namespace AIFitnessCoach.AI.Models;

public sealed class Conversation
{
    private Conversation()
    {
        UserId = string.Empty;
        Messages = [];
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int ContextVersion { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal EstimatedCost { get; private set; }
    public ICollection<Message> Messages { get; private set; }

    public static Conversation Create(string userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        return new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
            ContextVersion = 1
        };
    }

    public void RecordUsage(int inputTokens, int outputTokens, decimal estimatedCost, DateTimeOffset now)
    {
        if (inputTokens < 0 || outputTokens < 0 || estimatedCost < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens), "Token usage and cost must be non-negative.");
        InputTokens = checked(InputTokens + inputTokens);
        OutputTokens = checked(OutputTokens + outputTokens);
        EstimatedCost += estimatedCost;
        UpdatedAt = now.ToUniversalTime();
    }

    public void SetContextVersion(int contextVersion, DateTimeOffset now)
    {
        if (contextVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(contextVersion));
        ContextVersion = contextVersion;
        UpdatedAt = now.ToUniversalTime();
    }
}
