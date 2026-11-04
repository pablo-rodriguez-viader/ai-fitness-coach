namespace AIFitnessCoach.AI.Models;

public sealed record ConversationSummary(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextVersion,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost,
    string? LastMessagePreview);

public sealed record ConversationDetail(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextVersion,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost,
    IReadOnlyList<MessageView> Messages);

public sealed record MessageView(
    Guid Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
