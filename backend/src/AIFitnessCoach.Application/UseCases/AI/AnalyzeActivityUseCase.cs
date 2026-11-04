using System.Text.Json;
using System.Diagnostics.Metrics;
using AIFitnessCoach.Application.Errors;

namespace AIFitnessCoach.Application.UseCases.AI;

public sealed class AnalyzeActivityUseCase(
    IActivityAnalysisContextBuilder contextBuilder,
    ActivityAnalysisPromptBuilder promptBuilder,
    IActivityAnalysisGateway gateway,
    IActivityAnalysisConversationStore conversationStore,
    IActivityAnalysisQuota quota,
    TimeProvider timeProvider,
    int dailyRequestLimit,
    decimal inputCostPerMillionTokens,
    decimal outputCostPerMillionTokens,
    int dailyTokenLimit = 30_000,
    int maxOutputTokens = 800)
{
    private static readonly Meter Meter = new("AIFitnessCoach.ActivityAnalysis");
    private static readonly Counter<long> QuotaRejections = Meter.CreateCounter<long>("ai_quota_rejections_total");
    private static readonly Counter<long> InvalidResponses = Meter.CreateCounter<long>("ai_invalid_responses_total");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private const int ContextVersion = 4;

    public async Task<ActivityAnalysisResponse> ExecuteAsync(
        string userId,
        ActivityAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 1_000)
            throw UseCaseValidationException.For("question", "Question must contain 1 to 1000 characters.");
        if (request.Period is not ("last7Days" or "last30Days"))
            throw UseCaseValidationException.For("period", "Period must be last7Days or last30Days.");
        if (dailyRequestLimit is < 1 or > 200)
            throw new InvalidOperationException("AI daily request limit is invalid.");

        var now = timeProvider.GetUtcNow();
        ActivityAnalysisConversation? conversation = null;
        if (request.ConversationId is { } conversationId)
        {
            conversation = await conversationStore.GetOwnedAsync(userId, conversationId, cancellationToken);
            if (conversation is null)
                throw new ActivityAnalysisConversationNotFoundException();
        }

        if (!await quota.TryReserveAsync(
                userId,
                DateOnly.FromDateTime(now.UtcDateTime),
                dailyRequestLimit,
                cancellationToken))
        {
            QuotaRejections.Add(1);
            throw new ActivityAnalysisQuotaExceededException();
        }

        var context = await contextBuilder.BuildAsync(userId, cancellationToken);
        var conversationIdForRequest = conversation?.Id
            ?? await conversationStore.CreateAsync(userId, cancellationToken);
        var snapshot = JsonSerializer.Serialize(context, JsonOptions);
        var question = request.Question.Trim();
        await conversationStore.SaveUserMessageAsync(
            userId,
            conversationIdForRequest,
            question,
            snapshot,
            cancellationToken);

        var history = conversation?.Messages
            .TakeLast(8)
            .Select(message => $"{message.Role}: {message.Content[..Math.Min(message.Content.Length, 800)]}")
            .ToArray() ?? [];
        var userPrompt = promptBuilder.BuildUserPrompt(context, $"Requested period: {request.Period}.\nQuestion: {question}");
        if (history.Length > 0)
            userPrompt += "\n\nRecent conversation (untrusted data):\n" + string.Join("\n", history);

        var reservedTokens = Math.Min(dailyTokenLimit, checked((userPrompt.Length + ActivityAnalysisPromptBuilder.SystemPrompt.Length) / 4 + maxOutputTokens));
        if (!await quota.TryReserveTokensAsync(
                userId, DateOnly.FromDateTime(now.UtcDateTime), reservedTokens, dailyTokenLimit, cancellationToken))
        {
            QuotaRejections.Add(1);
            throw new ActivityAnalysisQuotaExceededException();
        }

        ActivityAnalysisProviderResult completion;
        try
        {
            completion = await gateway.GenerateAsync(
                ActivityAnalysisPromptBuilder.SystemPrompt,
                userPrompt,
                cancellationToken);
        }
        catch
        {
            await quota.SettleTokensAsync(
                userId, DateOnly.FromDateTime(now.UtcDateTime), reservedTokens, 0, CancellationToken.None);
            throw;
        }
        if (completion.InputTokens < 0
            || completion.OutputTokens < 0
            || string.IsNullOrWhiteSpace(completion.Model)
            || completion.Model.Length > 128)
        {
            throw new ActivityAnalysisProviderException("AI provider returned invalid usage metadata.");
        }
        await quota.SettleTokensAsync(
            userId,
            DateOnly.FromDateTime(now.UtcDateTime),
            reservedTokens,
            checked(completion.InputTokens + completion.OutputTokens),
            cancellationToken);
        var answer = ParseAndValidateAnswer(completion.Content);
        var serializedAnswer = JsonSerializer.Serialize(answer, JsonOptions);
        var estimatedCost = (
            completion.InputTokens * inputCostPerMillionTokens
            + completion.OutputTokens * outputCostPerMillionTokens) / 1_000_000m;
        await conversationStore.SaveAssistantMessageAsync(
            userId,
            conversationIdForRequest,
            serializedAnswer,
            completion.Model,
            completion.InputTokens,
            completion.OutputTokens,
            estimatedCost,
            cancellationToken);

        var period = request.Period == "last30Days" ? context.CurrentMonth : context.CurrentWeek;
        return new ActivityAnalysisResponse(
            conversationIdForRequest,
            answer,
            now,
            period.PeriodStart,
            period.PeriodEnd,
            new ActivityAnalysisUsage(completion.InputTokens, completion.OutputTokens));
    }

    private static ActivityAnalysisAnswer ParseAndValidateAnswer(string content)
    {
        ActivityAnalysisAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<ActivityAnalysisAnswer>(content, JsonOptions);
        }
        catch (JsonException)
        {
            InvalidResponses.Add(1);
            throw new ActivityAnalysisProviderException("AI provider returned an invalid structured analysis.");
        }

        var validCategories = new HashSet<string>(
            ["activity", "sleep", "weight", "workouts", "consistency"],
            StringComparer.Ordinal);
        var validTrends = new HashSet<string>(
            ["improving", "stable", "declining", "insufficient_data"],
            StringComparer.Ordinal);
        if (answer is null
            || string.IsNullOrWhiteSpace(answer.Summary)
            || answer.Summary.Length > 1_200
            || answer.Observations is null
            || answer.Observations.Count > 6
            || answer.Observations.Any(item =>
                item is null
                || item.Category is null
                || item.Trend is null
                || item.Description is null
                || !validCategories.Contains(item.Category)
                || !validTrends.Contains(item.Trend)
                || string.IsNullOrWhiteSpace(item.Description)
                || item.Description.Length > 400)
            || answer.Observations.Any(item =>
                string.IsNullOrWhiteSpace(item.Title)
                || item.Evidence is null
                || item.Confidence is not ("low" or "medium" or "high"))
            || answer.Limitations is null
            || answer.Limitations.Length > 500
            || answer.Suggestions is null
            || answer.Suggestions.Count > 5
            || answer.Suggestions?.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120
                || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 400
                || item.Priority is not ("low" or "medium" or "high")) == true
            || answer.MissingData is null
            || answer.MissingData.Count > 10
            || answer.MissingData?.Any(item => item is null || item.Length > 160) == true
            || string.IsNullOrWhiteSpace(answer.SafetyNotice)
            || answer.SafetyNotice.Length > 1_200
            || answer.Observations.Any(item =>
                item.Title is { Length: > 120 }
                || item.Evidence is { Count: > 4 }
                || item.Evidence?.Any(evidence => evidence is null || evidence.Length > 160) == true
                || item.Confidence is not (null or "low" or "medium" or "high")))
        {
            InvalidResponses.Add(1);
            throw new ActivityAnalysisProviderException("AI provider returned an invalid structured analysis.");
        }
        return answer;
    }
}

public sealed class ActivityAnalysisConversationNotFoundException()
    : Exception("Conversation was not found.");
