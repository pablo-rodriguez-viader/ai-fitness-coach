namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record CoachConversationSummaryResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextVersion,
    string? LastMessagePreview);

public sealed record CoachConversationResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextVersion,
    IReadOnlyList<CoachConversationMessageResponse> Messages);

public sealed record CoachConversationMessageResponse(
    Guid Id,
    string Role,
    string Content,
    AIFitnessCoach.Application.UseCases.AI.CoachAnalysis? Analysis,
    DateTimeOffset CreatedAt);
