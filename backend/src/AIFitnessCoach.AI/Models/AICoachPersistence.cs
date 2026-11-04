namespace AIFitnessCoach.AI.Models;

public sealed class AIConsent
{
    private AIConsent() { }

    public string UserId { get; private set; } = string.Empty;
    public bool Granted { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset? GrantedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AIConsent Set(string userId, bool granted, int version, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128 || version < 1)
            throw new ArgumentException("Consent user and version must be valid.");
        return new AIConsent
        {
            UserId = userId,
            Granted = granted,
            Version = version,
            GrantedAt = granted ? now.ToUniversalTime() : null,
            UpdatedAt = now.ToUniversalTime()
        };
    }

    public void Update(bool granted, int version, DateTimeOffset now)
    {
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        Granted = granted;
        Version = version;
        GrantedAt = granted ? now.ToUniversalTime() : null;
        UpdatedAt = now.ToUniversalTime();
    }
}

public sealed class AICoachRequest
{
    private AICoachRequest() { }

    public string UserId { get; private set; } = string.Empty;
    public Guid ClientRequestId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public string Status { get; private set; } = "Processing";
    public string? ResponseJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static AICoachRequest Start(string userId, Guid clientRequestId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128 || clientRequestId == Guid.Empty)
            throw new ArgumentException("User and client request id must be valid.");
        return new AICoachRequest
        {
            UserId = userId,
            ClientRequestId = clientRequestId,
            CreatedAt = now.ToUniversalTime()
        };
    }

    public void Complete(Guid conversationId, string responseJson, DateTimeOffset now)
    {
        if (conversationId == Guid.Empty || string.IsNullOrWhiteSpace(responseJson) || responseJson.Length > 20_000)
            throw new ArgumentException("A valid idempotent response is required.", nameof(responseJson));
        ConversationId = conversationId;
        Status = "Completed";
        ResponseJson = responseJson;
        CompletedAt = now.ToUniversalTime();
    }
}

public sealed class AIConversationSummaryRecord
{
    private AIConversationSummaryRecord() { }

    public Guid ConversationId { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public int ContextVersion { get; private set; }
    public string PromptVersion { get; private set; } = string.Empty;
    public int MessageCountIncluded { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AIConversationSummaryRecord Create(
        Guid conversationId,
        string summary,
        int contextVersion,
        string promptVersion,
        int messageCountIncluded,
        DateTimeOffset now)
    {
        if (conversationId == Guid.Empty || summary.Length > 1_200 || contextVersion < 1
            || string.IsNullOrWhiteSpace(promptVersion) || promptVersion.Length > 32
            || messageCountIncluded < 0)
            throw new ArgumentException("Conversation summary values are invalid.");
        return new AIConversationSummaryRecord
        {
            ConversationId = conversationId,
            Summary = summary,
            ContextVersion = contextVersion,
            PromptVersion = promptVersion,
            MessageCountIncluded = messageCountIncluded,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime()
        };
    }

    public void Update(string summary, int contextVersion, string promptVersion, int messageCount, DateTimeOffset now)
    {
        if (summary.Length > 1_200 || contextVersion < 1 || promptVersion.Length > 32 || messageCount < 0)
            throw new ArgumentException("Conversation summary values are invalid.");
        Summary = summary;
        ContextVersion = contextVersion;
        PromptVersion = promptVersion;
        MessageCountIncluded = messageCount;
        UpdatedAt = now.ToUniversalTime();
    }
}

public sealed class AIFeedback
{
    private AIFeedback() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid ConversationId { get; private set; }
    public Guid MessageId { get; private set; }
    public string Rating { get; private set; } = string.Empty;
    public string? ReasonCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AIFeedback Create(
        string userId,
        Guid conversationId,
        Guid messageId,
        string rating,
        string? reasonCode,
        DateTimeOffset now)
    {
        var validReasons = new HashSet<string>(
            ["Helpful", "NotHelpful", "Incorrect", "Irrelevant", "Unsafe", "MissingContext", "HardToUnderstand"],
            StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128
            || conversationId == Guid.Empty || messageId == Guid.Empty
            || rating is not ("Helpful" or "NotHelpful")
            || reasonCode is not null && !validReasons.Contains(reasonCode))
            throw new ArgumentException("AI feedback values are invalid.");
        return new AIFeedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConversationId = conversationId,
            MessageId = messageId,
            Rating = rating,
            ReasonCode = reasonCode,
            CreatedAt = now.ToUniversalTime()
        };
    }
}
