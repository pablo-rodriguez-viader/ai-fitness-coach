namespace AIFitnessCoach.Application.UseCases.AI;

public sealed class CreateCoachConversationUseCase(
    IActivityAnalysisConversationStore conversationStore,
    TimeProvider timeProvider,
    int maximumActiveConversations)
{
    public async Task<Guid> ExecuteAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
        var now = timeProvider.GetUtcNow();
        if (!await conversationStore.CanCreateConversationAsync(
                userId, now.AddDays(-30), maximumActiveConversations, cancellationToken))
            throw new CoachActiveConversationLimitException();
        return await conversationStore.CreateAsync(userId, cancellationToken);
    }
}
