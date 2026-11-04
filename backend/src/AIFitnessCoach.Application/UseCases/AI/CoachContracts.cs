namespace AIFitnessCoach.Application.UseCases.AI;

public enum CoachIntent
{
    WeeklyActivityAnalysis,
    MonthlyProgressAnalysis,
    StepTrendAnalysis,
    SleepTrendAnalysis,
    WeightTrendAnalysis,
    WorkoutConsistencyAnalysis,
    GeneralWellnessQuestion,
    DataAvailabilityQuestion,
    ConversationFollowUp,
    MedicalOrClinicalQuestion,
    UnsupportedRequest,
    PromptInjectionAttempt
}

public sealed record CoachMessageRequest(
    Guid? ConversationId,
    Guid ClientRequestId,
    string Message,
    string Period = "auto");

public sealed record CoachEvidence(string Metric, string Value, string Period);

public sealed record CoachObservation(
    string Title,
    string Description,
    IReadOnlyList<CoachEvidence> Evidence,
    string Confidence);

public sealed record CoachSuggestion(
    string Title,
    string Description,
    string Priority,
    bool RequiresConfirmation);

public sealed record CoachAnalysis(
    string Answer,
    string Intent,
    IReadOnlyList<CoachObservation> Observations,
    IReadOnlyList<CoachSuggestion> Suggestions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<string> FollowUpQuestions,
    string? SafetyNotice);

public sealed record CoachMessageResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid ClientRequestId,
    CoachAnalysis Analysis,
    CoachContextPeriod ContextPeriod,
    DateTimeOffset GeneratedAt,
    CoachTokenUsage Usage,
    string CorrelationId);

public sealed record CoachContextPeriod(DateTimeOffset Start, DateTimeOffset End);

public sealed record CoachTokenUsage(int InputTokens, int OutputTokens);

public sealed record CoachIntentPolicy(
    IReadOnlyList<string> AllowedContextCategories,
    string DefaultPeriod,
    bool IsBlocked,
    string? SafetyNotice);

public sealed record CoachContextSelection(
    CoachIntent Intent,
    IReadOnlyList<string> IncludedCategories,
    IReadOnlyList<string> OmittedCategories,
    IReadOnlyList<string> MissingDataCategories,
    string DataJson,
    string Period,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int Version);

public sealed record CoachConsent(bool Granted, int Version, DateTimeOffset? GrantedAt);

/// <summary>
/// Version of the AI consent text. v2 names the third-party provider (OpenAI), as App Store
/// Guideline 5.1.2(i) requires. A consent given to an older text no longer counts: users are
/// asked again before any data reaches the provider.
/// </summary>
public static class CoachConsentVersion
{
    public const int Current = 2;
}

public interface ICoachConsentStore
{
    Task<CoachConsent> GetAsync(string userId, CancellationToken cancellationToken);
    Task SetAsync(string userId, bool granted, int version, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ICoachIdempotencyStore
{
    Task<CoachMessageResponse?> GetCompletedAsync(
        string userId,
        Guid clientRequestId,
        CancellationToken cancellationToken);
    Task<bool> TryBeginAsync(string userId, Guid clientRequestId, CancellationToken cancellationToken);
    Task CompleteAsync(
        string userId,
        Guid clientRequestId,
        CoachMessageResponse response,
        CancellationToken cancellationToken);
    Task AbandonAsync(string userId, Guid clientRequestId, CancellationToken cancellationToken);
}

public interface ICoachFeedbackStore
{
    Task<bool> AddAsync(
        string userId,
        Guid conversationId,
        Guid messageId,
        string rating,
        string? reasonCode,
        string? comment,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ICoachMemoryStore
{
    Task<CoachConversationMemory?> GetSummaryAsync(
        string userId,
        Guid conversationId,
        int contextVersion,
        CancellationToken cancellationToken);
    Task SaveSummaryAsync(
        string userId,
        Guid conversationId,
        string summary,
        int contextVersion,
        string promptVersion,
        int messageCount,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record CoachConversationMemory(string Summary, int MessageCountIncluded);

public sealed class CoachConsentRequiredException()
    : Exception("Consent is required before the Coach can process health context.");

public sealed class CoachConversationConflictException()
    : Exception("The conversation is currently processing another request.");

public sealed class CoachConversationLimitException()
    : Exception("The Coach conversation limit has been reached.");

public sealed class CoachTokenQuotaExceededException()
    : Exception("The daily AI Coach token limit has been reached.");

public sealed class CoachActiveConversationLimitException()
    : Exception("The active Coach conversation limit has been reached.");

public sealed class CoachMessageNotFoundException()
    : Exception("The coach message was not found.");
