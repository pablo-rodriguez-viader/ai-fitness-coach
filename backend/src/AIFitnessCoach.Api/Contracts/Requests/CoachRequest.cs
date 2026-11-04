namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record CoachRequest(Guid? ConversationId, string? Question);
