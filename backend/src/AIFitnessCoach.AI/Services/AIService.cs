using System.Text.Json;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using Microsoft.Extensions.Options;

namespace AIFitnessCoach.AI.Services;

public sealed class AIService(
    AIContextBuilder contextBuilder,
    PromptBuilder promptBuilder,
    ConversationService conversationService,
    IAIConversationRepository repository,
    IOpenAIClient openAIClient,
    TokenUsageService tokenUsageService,
    IOptions<AIOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ChatResponse> ChatAsync(
        string userId,
        ChatCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(command.Content) || command.Content.Trim().Length > 4_000)
            throw new ArgumentException("Message content must contain 1 to 4000 characters.", nameof(command));
        if (!options.Value.Enabled)
            throw new AIProviderException("AI chat is currently disabled.");

        var conversation = command.ConversationId is { } conversationId
            ? await conversationService.GetOwnedAsync(userId, conversationId, cancellationToken)
                ?? throw new AIConversationNotFoundException()
            : null;
        var context = await contextBuilder.BuildAsync(
            userId,
            options.Value.ContextLookbackDays,
            cancellationToken);
        conversation ??= await conversationService.CreateAsync(userId, cancellationToken);
        var snapshotJson = JsonSerializer.Serialize(context);
        var now = timeProvider.GetUtcNow();
        var userMessage = Message.Create(conversation.Id, MessageRole.User, command.Content, now);
        var snapshot = AIContextSnapshot.Create(
            conversation.Id,
            userId,
            context.Version,
            snapshotJson,
            now);
        var promptHistory = conversation.Messages
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .TakeLast(options.Value.ConversationHistoryMessages)
            .ToArray();
        await repository.AddUserMessageAsync(
            userId,
            conversation.Id,
            userMessage.Content,
            snapshot,
            now,
            cancellationToken);

        var prompt = promptBuilder.Build(
            context,
            promptHistory,
            userMessage.Content,
            options.Value.MaxConversationHistoryCharacters);
        var completion = await openAIClient.CompleteAsync(prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(completion.Content))
            throw new AIProviderException("The AI provider returned an empty response.");
        var savedMessage = await tokenUsageService.RecordAsync(
            userId,
            conversation.Id,
            completion.Content,
            completion,
            cancellationToken);

        return new ChatResponse(
            conversation.Id,
            new MessageView(
                savedMessage.Id,
                savedMessage.Role.ToString().ToLowerInvariant(),
                savedMessage.Content,
                savedMessage.CreatedAt),
            completion.InputTokens,
            completion.OutputTokens,
            tokenUsageService.EstimateCost(completion));
    }
}
