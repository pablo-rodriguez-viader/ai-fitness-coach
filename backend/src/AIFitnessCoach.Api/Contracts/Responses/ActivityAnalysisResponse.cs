using AIFitnessCoach.Application.UseCases.AI;

namespace AIFitnessCoach.Api.Contracts.Responses;

public sealed record ActivityAnalysisApiResponse(
    Guid ConversationId,
    ActivityAnalysisAnswer Analysis,
    DateTimeOffset GeneratedAt,
    ActivityAnalysisContextPeriod ContextPeriod,
    string CorrelationId,
    ActivityAnalysisUsage Usage)
{
    public static ActivityAnalysisApiResponse From(ActivityAnalysisResponse response, string correlationId) =>
        new(response.ConversationId, response.Analysis, response.GeneratedAt,
            new ActivityAnalysisContextPeriod(response.ContextPeriodStart, response.ContextPeriodEnd), correlationId,
            response.Usage);
}

public sealed record ActivityAnalysisContextPeriod(DateTimeOffset Start, DateTimeOffset End);
