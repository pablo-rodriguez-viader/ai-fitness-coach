using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using AIFitnessCoach.Application.Errors;

namespace AIFitnessCoach.Application.UseCases.AI;

public sealed class SendCoachMessageUseCase(
    ICoachConsentStore consentStore,
    ICoachIdempotencyStore idempotencyStore,
    IActivityAnalysisContextBuilder contextBuilder,
    CoachContextSelector contextSelector,
    CoachIntentClassifier intentClassifier,
    CoachPromptBuilder promptBuilder,
    IActivityAnalysisGateway gateway,
    IActivityAnalysisConversationStore conversationStore,
    ICoachMemoryStore memoryStore,
    IActivityAnalysisQuota quota,
    TimeProvider timeProvider,
    int dailyRequestLimit,
    int dailyTokenLimit,
    int maxOutputTokens,
    int maximumActiveConversations,
    int maximumConversationMessages,
    decimal inputCostPerMillionTokens,
    decimal outputCostPerMillionTokens)
{
    private static readonly Meter Meter = new("AIFitnessCoach.Coach");
    private static readonly Counter<long> Messages = Meter.CreateCounter<long>("coach_messages_total");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("coach_messages_failed_total");
    private static readonly Counter<long> BlockedRequests = Meter.CreateCounter<long>("coach_blocked_requests_total");
    private static readonly Counter<long> IdempotentReplays = Meter.CreateCounter<long>("coach_idempotent_replays_total");
    private static readonly Counter<long> MissingDataResponses = Meter.CreateCounter<long>("coach_missing_data_responses_total");
    private static readonly Counter<long> ConversationSummaries =
        Meter.CreateCounter<long>("coach_conversation_summary_total");
    private static readonly Counter<long> Intents = Meter.CreateCounter<long>("coach_intent_total");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("coach_message_duration", "ms");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<CoachMessageResponse> ExecuteAsync(
        string userId,
        CoachMessageRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "failed";
        var intentTag = "unknown";
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message) || message.Length > 1_000)
            throw UseCaseValidationException.For("message", "Message must contain 1 to 1000 characters.");
        if (request.ClientRequestId == Guid.Empty)
            throw UseCaseValidationException.For("clientRequestId", "A non-empty client request id is required.");
        if (request.Period is not ("auto" or "last7Days" or "last30Days"))
            throw UseCaseValidationException.For("period", "Period must be auto, last7Days or last30Days.");
        if (dailyRequestLimit is < 1 or > 200 || dailyTokenLimit is < 1
            || maxOutputTokens is < 1 || maximumActiveConversations is < 1
            || maximumConversationMessages is < 12 or > 200)
            throw new InvalidOperationException("AI Coach usage limits are invalid.");

        var consent = await consentStore.GetAsync(userId, cancellationToken);
        if (!consent.Granted)
            throw new CoachConsentRequiredException();

        var replay = await idempotencyStore.GetCompletedAsync(userId, request.ClientRequestId, cancellationToken);
        if (replay is not null)
        {
            if (request.ConversationId is { } expectedConversation
                && replay.ConversationId != expectedConversation)
                throw new ActivityAnalysisConversationNotFoundException();
            IdempotentReplays.Add(1);
            return replay;
        }

        ActivityAnalysisConversation? conversation = null;
        if (request.ConversationId is { } conversationId)
        {
            conversation = await conversationStore.GetOwnedAsync(userId, conversationId, cancellationToken);
            if (conversation is null)
                throw new ActivityAnalysisConversationNotFoundException();
            if (conversation.Messages.Count >= maximumConversationMessages)
                throw new CoachConversationLimitException();
        }

        if (!await idempotencyStore.TryBeginAsync(userId, request.ClientRequestId, cancellationToken))
        {
            replay = await idempotencyStore.GetCompletedAsync(userId, request.ClientRequestId, cancellationToken);
            return replay ?? throw new CoachConversationConflictException();
        }

        var reservationCompleted = false;
        var providerInvoked = false;
        var tokensReserved = false;
        var tokensSettled = false;
        var reservedTokens = 0;
        var usageDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            var now = timeProvider.GetUtcNow();
            usageDate = DateOnly.FromDateTime(now.UtcDateTime);
            if (!await quota.TryReserveAsync(
                    userId,
                    usageDate,
                    dailyRequestLimit,
                    cancellationToken))
            {
                throw new ActivityAnalysisQuotaExceededException();
            }

            var intent = intentClassifier.Classify(message, conversation is not null);
            intentTag = intent.ToString();
            Intents.Add(1, new KeyValuePair<string, object?>("intent", intentTag));
            var policy = CoachIntentClassifier.PolicyFor(intent);
            CoachContextSelection selection;
            CoachAnalysis analysis;
            var inputTokens = 0;
            var outputTokens = 0;
            var model = "local-policy";
            var cost = 0m;
            string snapshotJson;
            Guid? conversationIdForRequest = conversation?.Id;
            if (conversation is null
                && !await conversationStore.CanCreateConversationAsync(
                    userId, now.AddDays(-30), maximumActiveConversations, cancellationToken))
                throw new CoachConversationLimitException();
            var history = conversation?.Messages.TakeLast(6).ToArray() ?? [];
            var memory = conversation is null
                ? null
                : await memoryStore.GetSummaryAsync(
                    userId, conversation.Id, CoachPromptBuilder.ContextVersion, cancellationToken);

            if (policy.IsBlocked)
            {
                BlockedRequests.Add(1, new KeyValuePair<string, object?>("intent", intentTag));
                selection = new CoachContextSelection(intent, [], ["all"], [], "{}", "auto",
                    now, now,
                    CoachPromptBuilder.ContextVersion);
                analysis = new CoachAnalysis(
                    policy.SafetyNotice ?? "No puedo ayudar con esa solicitud.",
                    intent.ToString(), [], [], [], [],
                    policy.SafetyNotice);
                snapshotJson = "{}";
            }
            else
            {
                var context = await contextBuilder.BuildAsync(userId, cancellationToken);
                selection = contextSelector.Select(context, intent, request.Period);
                snapshotJson = JsonSerializer.Serialize(new
                {
                    promptVersion = CoachPromptBuilder.PromptVersion,
                    contextVersion = CoachPromptBuilder.ContextVersion,
                    selection
                }, JsonOptions);
                var prompt = promptBuilder.BuildUserPrompt(selection, memory?.Summary, history, message);
                reservedTokens = checked((prompt.Length + ActivityAnalysisPromptBuilder.SystemPrompt.Length + 2) / 3
                    + maxOutputTokens);
                if (!await quota.TryReserveTokensAsync(
                        userId,
                        usageDate,
                        reservedTokens,
                        dailyTokenLimit,
                        cancellationToken))
                    throw new CoachTokenQuotaExceededException();
                tokensReserved = true;
                providerInvoked = true;
                var completion = await gateway.GenerateAsync(
                    CoachPromptBuilder.SystemPrompt,
                    prompt,
                    cancellationToken);
                analysis = ParseAndValidate(completion.Content, intent);
                ValidateEvidence(analysis, selection);
                analysis = analysis with
                {
                    MissingData = analysis.MissingData
                        .Concat(selection.MissingDataCategories)
                        .Distinct(StringComparer.Ordinal)
                        .Take(5)
                        .ToArray()
                };
                if (completion.InputTokens < 0 || completion.OutputTokens < 0
                    || string.IsNullOrWhiteSpace(completion.Model) || completion.Model.Length > 128)
                    throw new ActivityAnalysisProviderException("AI provider returned invalid usage metadata.");
                inputTokens = completion.InputTokens;
                outputTokens = completion.OutputTokens;
                await quota.SettleTokensAsync(
                    userId,
                    usageDate,
                    reservedTokens,
                    checked(inputTokens + outputTokens),
                    cancellationToken);
                tokensSettled = true;
                model = completion.Model;
                cost = (inputTokens * inputCostPerMillionTokens + outputTokens * outputCostPerMillionTokens)
                    / 1_000_000m;
            }

            var persistedConversationId = conversationIdForRequest
                ?? await conversationStore.CreateAsync(userId, cancellationToken);
            await conversationStore.SaveUserMessageAsync(
                userId,
                persistedConversationId,
                message,
                snapshotJson,
                cancellationToken,
                CoachPromptBuilder.ContextVersion);
            var responseContent = JsonSerializer.Serialize(analysis, JsonOptions);
            var messageId = await conversationStore.SaveAssistantMessageAsync(
                userId, persistedConversationId, responseContent, model,
                inputTokens, outputTokens, cost, cancellationToken);
            var response = new CoachMessageResponse(
                persistedConversationId,
                messageId,
                request.ClientRequestId,
                analysis,
                new CoachContextPeriod(selection.PeriodStart, selection.PeriodEnd),
                now,
                new CoachTokenUsage(inputTokens, outputTokens),
                Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"));
            if (analysis.MissingData.Count > 0)
                MissingDataResponses.Add(1, new KeyValuePair<string, object?>("intent", intentTag));
            Messages.Add(1,
                new KeyValuePair<string, object?>("status", "success"),
                new KeyValuePair<string, object?>("intent", intentTag));
            await idempotencyStore.CompleteAsync(
                userId, request.ClientRequestId, response, cancellationToken);
            reservationCompleted = true;
            outcome = "success";
            var nextMessageCount = Math.Max(
                conversation?.Messages.Count ?? 0,
                memory?.MessageCountIncluded ?? 0) + 2;
            if (nextMessageCount >= 12 && nextMessageCount % 12 == 0)
            {
                var summaryText = string.Join(
                    "; ",
                    new[] { memory?.Summary, $"Temas: {intent}; periodo: {selection.Period}; métricas: {string.Join(", ", selection.IncludedCategories)}" }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (summaryText.Length > 1_200)
                    summaryText = summaryText[^1_200..];
                await memoryStore.SaveSummaryAsync(
                    userId,
                    persistedConversationId,
                    summaryText,
                    CoachPromptBuilder.ContextVersion,
                    CoachPromptBuilder.PromptVersion,
                    nextMessageCount,
                    now,
                    cancellationToken);
                ConversationSummaries.Add(
                    1,
                    new KeyValuePair<string, object?>("prompt_version", CoachPromptBuilder.PromptVersion),
                    new KeyValuePair<string, object?>("context_version", CoachPromptBuilder.ContextVersion));
            }
            return response;
        }
        finally
        {
            if (!reservationCompleted && !providerInvoked)
                await idempotencyStore.AbandonAsync(userId, request.ClientRequestId, CancellationToken.None);
            if (tokensReserved && !tokensSettled)
                await quota.SettleTokensAsync(
                    userId,
                    usageDate,
                    reservedTokens,
                    0,
                    CancellationToken.None);
            if (!reservationCompleted)
                Failures.Add(1, new KeyValuePair<string, object?>("intent", intentTag));
            Duration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                new KeyValuePair<string, object?>("state", outcome),
                new KeyValuePair<string, object?>("intent", intentTag));
        }
    }

    private static CoachAnalysis ParseAndValidate(string content, CoachIntent intent)
    {
        CoachAnalysis? answer;
        try
        {
            answer = JsonSerializer.Deserialize<CoachAnalysis>(content, JsonOptions);
        }
        catch (JsonException)
        {
            throw new ActivityAnalysisProviderException("AI provider returned an invalid Coach response.");
        }

        if (answer is null
            || string.IsNullOrWhiteSpace(answer.Answer) || answer.Answer.Length > 1_500
            || !string.Equals(answer.Intent, intent.ToString(), StringComparison.Ordinal)
            || answer.Observations is null || answer.Observations.Count > 4
            || answer.Observations.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 100
                || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 400
                || item.Evidence is null || item.Evidence.Count > 3
                || item.Evidence.Any(evidence => evidence is null
                    || string.IsNullOrWhiteSpace(evidence.Metric) || evidence.Metric.Length > 50
                    || string.IsNullOrWhiteSpace(evidence.Value) || evidence.Value.Length > 80
                    || string.IsNullOrWhiteSpace(evidence.Period) || evidence.Period.Length > 50)
                || item.Confidence is not ("low" or "medium" or "high"))
            || answer.Suggestions is null || answer.Suggestions.Count > 3
            || answer.Suggestions.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 100
                || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 400
                || item.Priority is not ("low" or "medium" or "high")
                || !item.RequiresConfirmation)
            || answer.MissingData is null || answer.MissingData.Count > 5
            || answer.MissingData.Any(item => item is null || item.Length > 120)
            || answer.FollowUpQuestions is null || answer.FollowUpQuestions.Count > 3
            || answer.FollowUpQuestions.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 160)
            || answer.SafetyNotice?.Length > 500)
        {
            throw new ActivityAnalysisProviderException("AI provider returned an invalid Coach response.");
        }
        return answer;
    }

    private static void ValidateEvidence(CoachAnalysis analysis, CoachContextSelection selection)
    {
        var availableNumbers = Regex.Matches(selection.DataJson, @"-?\d+(?:\.\d+)?")
            .Select(match => NormalizeNumber(match.Value))
            .ToHashSet(StringComparer.Ordinal);
        var evidence = analysis.Observations.SelectMany(item => item.Evidence);
        foreach (var item in evidence)
        {
            var numbers = Regex.Matches(item.Value, @"-?\d+(?:\.\d+)?")
                .Select(match => NormalizeNumber(match.Value));
            if (numbers.Any(number => !availableNumbers.Contains(number)))
                throw new ActivityAnalysisProviderException("AI provider returned evidence not present in the selected context.");
        }
    }

    private static string NormalizeNumber(string value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value;
}
