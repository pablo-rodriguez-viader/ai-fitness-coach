namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record AIChatRequest(Guid? ConversationId, string? Content);
