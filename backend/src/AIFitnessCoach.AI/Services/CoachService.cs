using AIFitnessCoach.AI.Contracts;

namespace AIFitnessCoach.AI.Services;

public sealed class CoachService(AIService aiService)
{
    public async Task<CoachResponse> AskAsync(
        string userId,
        CoachCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Question) || command.Question.Trim().Length > 4_000)
            throw new ArgumentException("Question must contain 1 to 4000 characters.", nameof(command));
        var response = await aiService.ChatAsync(
            userId,
            new ChatCommand(command.ConversationId, command.Question.Trim()),
            cancellationToken);
        return new CoachResponse(
            response.ConversationId,
            response.Message,
            response.InputTokens,
            response.OutputTokens,
            response.EstimatedCost);
    }
}
