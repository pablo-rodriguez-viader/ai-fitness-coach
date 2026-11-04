using System.Text.Json;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.Application.UseCases.AI;

namespace AIFitnessCoach.Infrastructure.AI;

public sealed class ActivityAnalysisConversationStore(
    IAIConversationRepository repository,
    TimeProvider timeProvider) : IActivityAnalysisConversationStore
{
    public async Task<bool> CanCreateConversationAsync(
        string userId,
        DateTimeOffset activeSince,
        int maximumActiveConversations,
        CancellationToken cancellationToken) =>
        await repository.CountOwnedSinceAsync(userId, activeSince, cancellationToken)
            < maximumActiveConversations;

    public async Task<ActivityAnalysisConversation?> GetOwnedAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await repository.GetOwnedAsync(userId, conversationId, cancellationToken);
        return conversation is null
            ? null
            : new ActivityAnalysisConversation(
                conversation.Id,
                conversation.Messages
                    .OrderBy(message => message.CreatedAt)
                    .Select(message => new ActivityAnalysisConversationMessage(
                        message.Role == MessageRole.User ? "user" : "assistant",
                        message.Content))
                    .ToArray());
    }

    public async Task<Guid> CreateAsync(string userId, CancellationToken cancellationToken)
    {
        var conversation = await repository.CreateAsync(userId, timeProvider.GetUtcNow(), cancellationToken);
        return conversation.Id;
    }

    public Task SaveUserMessageAsync(
        string userId,
        Guid conversationId,
        string question,
        string contextSnapshot,
        CancellationToken cancellationToken,
        int contextVersion = 4)
    {
        var snapshot = AIContextSnapshot.Create(
            conversationId,
            userId,
            contextVersion,
            contextSnapshot,
            timeProvider.GetUtcNow());
        return repository.AddUserMessageAsync(
            userId,
            conversationId,
            question,
            snapshot,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task<Guid> SaveAssistantMessageAsync(
        string userId,
        Guid conversationId,
        string response,
        string model,
        int inputTokens,
        int outputTokens,
        decimal estimatedCost,
        CancellationToken cancellationToken)
    {
        var completion = new AIChatCompletion(response, model, inputTokens, outputTokens);
        var message = await repository.AddAssistantMessageAsync(
            userId,
            conversationId,
            response,
            completion,
            estimatedCost,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return message.Id;
    }
}
