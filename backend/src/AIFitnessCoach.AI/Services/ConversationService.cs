using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class ConversationService(
    IAIConversationRepository repository,
    TimeProvider timeProvider)
{
    public Task<Conversation> CreateAsync(string userId, CancellationToken cancellationToken) =>
        repository.CreateAsync(userId, timeProvider.GetUtcNow(), cancellationToken);

    public Task<Conversation?> GetOwnedAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        repository.GetOwnedAsync(userId, conversationId, cancellationToken);

    public Task<IReadOnlyList<ConversationSummary>> ListAsync(
        string userId,
        int pageSize,
        CancellationToken cancellationToken) =>
        repository.ListOwnedAsync(userId, Math.Clamp(pageSize, 1, 100), cancellationToken);

    public Task<bool> DeleteAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        repository.DeleteOwnedAsync(userId, conversationId, cancellationToken);

    public Task<AIUsageSummary> GetUsageSummaryAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        repository.GetUsageSummaryAsync(userId, from, to, cancellationToken);
}
