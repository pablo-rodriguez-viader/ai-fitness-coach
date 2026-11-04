namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record ActivityAnalysisRequest(Guid? ConversationId, string? Question, string Period = "last7Days");
