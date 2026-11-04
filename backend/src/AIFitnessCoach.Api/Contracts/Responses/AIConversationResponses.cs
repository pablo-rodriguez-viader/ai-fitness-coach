using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record AIConversationResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextVersion,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost,
    IReadOnlyList<AIMessageResponse> Messages);

public sealed record AIMessageResponse(
    Guid Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record AIChatResponse(
    Guid ConversationId,
    AIMessageResponse Message,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost);

public static class AIConversationResponseMapper
{
    public static AIConversationResponse ToResponse(ConversationDetail detail) =>
        new(
            detail.Id,
            detail.CreatedAt,
            detail.UpdatedAt,
            detail.ContextVersion,
            detail.InputTokens,
            detail.OutputTokens,
            detail.EstimatedCost,
            detail.Messages.Select(message => new AIMessageResponse(
                message.Id,
                message.Role,
                message.Content,
                message.CreatedAt)).ToArray());

    public static AIChatResponse ToResponse(AIFitnessCoach.AI.Contracts.ChatResponse response) =>
        new(
            response.ConversationId,
            new AIMessageResponse(
                response.Message.Id,
                response.Message.Role,
                response.Message.Content,
                response.Message.CreatedAt),
            response.InputTokens,
            response.OutputTokens,
            response.EstimatedCost);
}
