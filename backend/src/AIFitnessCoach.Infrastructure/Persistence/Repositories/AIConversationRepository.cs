using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class AIConversationRepository(
    FitnessDbContext dbContext) : IAIConversationRepository
{
    public async Task<Conversation> CreateAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var conversation = Conversation.Create(userId, now);
        dbContext.AIConversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public Task<Conversation?> GetOwnedAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        dbContext.AIConversations
            .AsNoTracking()
            .Include(conversation => conversation.Messages
                .OrderByDescending(message => message.CreatedAt)
                .Take(200))
            .SingleOrDefaultAsync(
                conversation => conversation.UserId == userId && conversation.Id == conversationId,
                cancellationToken);

    public Task<int> CountOwnedSinceAsync(
        string userId,
        DateTimeOffset activeSince,
        CancellationToken cancellationToken) =>
        dbContext.AIConversations.CountAsync(
            conversation => conversation.UserId == userId && conversation.UpdatedAt >= activeSince,
            cancellationToken);

    public async Task<IReadOnlyList<ConversationSummary>> ListOwnedAsync(
        string userId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var results = await dbContext.AIConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Take(pageSize)
            .Select(conversation => new ConversationSummary(
                conversation.Id,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.ContextVersion,
                conversation.InputTokens,
                conversation.OutputTokens,
                conversation.EstimatedCost,
                conversation.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => message.Content)
                    .FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
        return results.Select(summary => summary with
        {
            LastMessagePreview = summary.LastMessagePreview is { Length: > 240 } content
                ? content[..240]
                : summary.LastMessagePreview
        }).ToArray();
    }

    public async Task<bool> DeleteOwnedAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.AIConversations
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.Id == conversationId,
                cancellationToken);
        if (conversation is null)
            return false;
        dbContext.AIConversations.Remove(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AIUsageSummary> GetUsageSummaryAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var usage = dbContext.AITokenUsage.AsNoTracking()
            .Where(item => item.UserId == userId && item.CreatedAt >= from && item.CreatedAt < to);
        var requests = await usage.CountAsync(cancellationToken);
        var inputTokens = await usage.SumAsync(item => (long)item.InputTokens, cancellationToken);
        var outputTokens = await usage.SumAsync(item => (long)item.OutputTokens, cancellationToken);
        var cost = requests == 0
            ? (decimal?)null
            : await usage.SumAsync(item => item.EstimatedCost, cancellationToken);
        return new AIUsageSummary(requests, inputTokens, outputTokens, cost);
    }

    public async Task AddUserMessageAsync(
        string userId,
        Guid conversationId,
        string content,
        AIContextSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var conversation = await GetTrackedOwnedAsync(userId, conversationId, cancellationToken);
        conversation.SetContextVersion(snapshot.ContextVersion, now);
        dbContext.AIMessages.Add(Message.Create(conversationId, MessageRole.User, content, now));
        dbContext.AIContextSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Message> AddAssistantMessageAsync(
        string userId,
        Guid conversationId,
        string content,
        AIChatCompletion completion,
        decimal estimatedCost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var conversation = await GetTrackedOwnedAsync(userId, conversationId, cancellationToken);
        var message = Message.Create(conversationId, MessageRole.Assistant, content, now);
        conversation.RecordUsage(completion.InputTokens, completion.OutputTokens, estimatedCost, now);
        dbContext.AIMessages.Add(message);
        dbContext.AITokenUsage.Add(AITokenUsage.Create(
            userId,
            conversationId,
            completion.Model,
            completion.InputTokens,
            completion.OutputTokens,
            estimatedCost,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    private async Task<Conversation> GetTrackedOwnedAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        await dbContext.AIConversations.SingleOrDefaultAsync(
            conversation => conversation.UserId == userId && conversation.Id == conversationId,
            cancellationToken)
        ?? throw new InvalidOperationException("The AI conversation is no longer available.");
}
