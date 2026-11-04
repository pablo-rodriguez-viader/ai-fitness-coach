namespace AIFitnessCoach.Application.UseCases.AI;

public sealed record ActivityAnalysisRequest(Guid? ConversationId, string Question, string Period = "last7Days");

public sealed record ActivityAnalysisResponse(
    Guid ConversationId,
    ActivityAnalysisAnswer Analysis,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ContextPeriodStart,
    DateTimeOffset ContextPeriodEnd,
    ActivityAnalysisUsage Usage);

public sealed record ActivityAnalysisUsage(int InputTokens, int OutputTokens);

public sealed record ActivityAnalysisAnswer(
    string Summary,
    IReadOnlyList<ActivityObservation> Observations,
    string Limitations,
    IReadOnlyList<ActivitySuggestion>? Suggestions = null,
    IReadOnlyList<string>? MissingData = null,
    string? SafetyNotice = null);

public sealed record ActivityObservation(
    string Category,
    string Trend,
    string Description,
    string? Title = null,
    IReadOnlyList<string>? Evidence = null,
    string? Confidence = null);

public sealed record ActivitySuggestion(string Title, string Description, string Priority);

public sealed record ActivityAnalysisProfile(int? Age);

public sealed record ActivityPeriodSummary(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    double? Steps,
    double? AverageSleepHours,
    double? WeightKilograms,
    int? Workouts,
    double? WorkoutMinutes,
    double? AverageHeartRate,
    int DaysWithSteps,
    int DaysWithWorkouts);

public sealed record ActivityMetricTrend(string Metric, double? Change, string Unit);

public sealed record ActivityWorkoutSummary(
    DateOnly Date,
    string Goal,
    bool Completed,
    string? Difficulty);

public sealed record ActivityAnalysisContext(
    ActivityAnalysisProfile? Profile,
    string? ActiveGoal,
    ActivityPeriodSummary CurrentWeek,
    ActivityPeriodSummary PreviousWeek,
    ActivityPeriodSummary CurrentMonth,
    IReadOnlyList<ActivityMetricTrend> Trends,
    string? Consistency,
    int ActiveDaysLastWeek,
    int ActiveDaysPreviousWeek,
    IReadOnlyList<ActivityWorkoutSummary> RecentWorkouts);

public sealed record ActivityAnalysisProviderResult(
    string Content,
    string Model,
    int InputTokens,
    int OutputTokens);

public sealed record ActivityAnalysisConversationMessage(string Role, string Content);

public sealed record ActivityAnalysisConversation(
    Guid Id,
    IReadOnlyList<ActivityAnalysisConversationMessage> Messages);

public sealed class ActivityAnalysisProviderException(string message) : Exception(message);

public sealed class ActivityAnalysisQuotaExceededException()
    : Exception("Daily AI activity-analysis limit has been reached.");

public interface IActivityAnalysisContextBuilder
{
    Task<ActivityAnalysisContext> BuildAsync(string userId, CancellationToken cancellationToken);
}

public interface IActivityAnalysisGateway
{
    Task<ActivityAnalysisProviderResult> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}

public interface IActivityAnalysisConversationStore
{
    Task<bool> CanCreateConversationAsync(
        string userId,
        DateTimeOffset activeSince,
        int maximumActiveConversations,
        CancellationToken cancellationToken);
    Task<ActivityAnalysisConversation?> GetOwnedAsync(
        string userId,
        Guid conversationId,
        CancellationToken cancellationToken);
    Task<Guid> CreateAsync(string userId, CancellationToken cancellationToken);
    Task SaveUserMessageAsync(
        string userId,
        Guid conversationId,
        string question,
        string contextSnapshot,
        CancellationToken cancellationToken,
        int contextVersion = 4);
    Task<Guid> SaveAssistantMessageAsync(
        string userId,
        Guid conversationId,
        string response,
        string model,
        int inputTokens,
        int outputTokens,
        decimal estimatedCost,
        CancellationToken cancellationToken);
}

public interface IActivityAnalysisQuota
{
    Task<bool> TryReserveAsync(string userId, DateOnly date, int dailyLimit, CancellationToken cancellationToken);
    Task<bool> TryReserveTokensAsync(
        string userId,
        DateOnly date,
        int tokenReserve,
        int dailyTokenLimit,
        CancellationToken cancellationToken);
    Task SettleTokensAsync(
        string userId,
        DateOnly date,
        int reservedTokens,
        int actualTokens,
        CancellationToken cancellationToken);
}
