using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Abstractions;

public interface IAIContextSource
{
    Task<AIUserContext> GetContextAsync(string userId, DateTimeOffset from, CancellationToken cancellationToken);
}

public interface IAIConversationRepository
{
    Task<Conversation> CreateAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<Conversation?> GetOwnedAsync(string userId, Guid conversationId, CancellationToken cancellationToken);
    Task<int> CountOwnedSinceAsync(
        string userId,
        DateTimeOffset activeSince,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationSummary>> ListOwnedAsync(
        string userId,
        int pageSize,
        CancellationToken cancellationToken);
    Task<bool> DeleteOwnedAsync(string userId, Guid conversationId, CancellationToken cancellationToken);
    Task<AIUsageSummary> GetUsageSummaryAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
    Task AddUserMessageAsync(
        string userId,
        Guid conversationId,
        string content,
        AIContextSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<Message> AddAssistantMessageAsync(
        string userId,
        Guid conversationId,
        string content,
        AIChatCompletion completion,
        decimal estimatedCost,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record AIUsageSummary(int Requests, long InputTokens, long OutputTokens, decimal? EstimatedCost);

public interface IOpenAIClient
{
    Task<AIChatCompletion> CompleteAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        CancellationToken cancellationToken);

    Task<AIChatCompletion> CompleteJsonAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        CancellationToken cancellationToken) =>
        CompleteAsync(messages, cancellationToken);
}
