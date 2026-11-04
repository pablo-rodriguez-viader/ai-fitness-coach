using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using Microsoft.Extensions.Options;
using AIFitnessCoach.AI.Configuration;

namespace AIFitnessCoach.AI.Services;

public sealed class TokenUsageService(
    IAIConversationRepository repository,
    IOptions<AIOptions> options,
    TimeProvider timeProvider)
{
    public decimal EstimateCost(AIChatCompletion completion)
    {
        var settings = options.Value;
        return (completion.InputTokens * settings.InputCostPerMillionTokens
                + completion.OutputTokens * settings.OutputCostPerMillionTokens)
            / 1_000_000m;
    }

    public Task<Message> RecordAsync(
        string userId,
        Guid conversationId,
        string assistantMessage,
        AIChatCompletion completion,
        CancellationToken cancellationToken) =>
        repository.AddAssistantMessageAsync(
            userId,
            conversationId,
            assistantMessage,
            completion,
            EstimateCost(completion),
            timeProvider.GetUtcNow(),
            cancellationToken);
}
