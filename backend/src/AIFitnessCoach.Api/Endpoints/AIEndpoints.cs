using System.Security.Claims;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Application.UseCases.AI;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Api.Contracts.Requests;
using AIFitnessCoach.Api.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Endpoints;

public static class AIEndpoints
{
    public static IEndpointRouteBuilder MapAIEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var ai = endpoints.MapGroup("/api/v1/ai").RequireAuthorization();
        ai.MapPost("/chat", () => NoStore(Results.Problem(
            title: "Use the authenticated AI Coach conversation API.",
            statusCode: StatusCodes.Status410Gone)));
        ai.MapPost("/coach", () => NoStore(Results.Problem(
            title: "Use the authenticated AI Coach conversation API.",
            statusCode: StatusCodes.Status410Gone)));
        ai.MapPost("/activity-analysis", AnalyzeActivity).RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        ai.MapPost("/health-analysis", AnalyzeActivity).RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        ai.MapPost("/conversations", CreateAIConversation).RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        ai.MapGet("/conversations", ListConversations);
        ai.MapGet("/conversations/{id:guid}", GetConversation);
        ai.MapDelete("/conversations/{id:guid}", DeleteConversation);
        ai.MapGet("/consent", GetCoachConsent);
        ai.MapPut("/consent", SetCoachConsent);
        ai.MapDelete("/consent", RevokeAIConsent);
        ai.MapGet("/usage/summary", GetAIUsageSummary);

        var coach = endpoints.MapGroup("/api/v1/coach").RequireAuthorization();
        coach.MapPost("/conversations", CreateCoachConversation);
        coach.MapGet("/conversations", ListCoachConversations);
        coach.MapGet("/conversations/{id:guid}", GetCoachConversation);
        coach.MapDelete("/conversations/{id:guid}", DeleteCoachConversation);
        coach.MapPost("/conversations/{id:guid}/messages", SendCoachMessage)
            .RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        coach.MapPost("/conversations/{id:guid}/feedback", SubmitCoachFeedback);
        coach.MapGet("/suggested-questions", SuggestedCoachQuestions);
        coach.MapGet("/consent", GetCoachConsent);
        coach.MapPut("/consent", SetCoachConsent);
        return endpoints;
    }

    private static async Task<IResult> ListCoachConversations(
        int? limit,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var items = await service.ListAsync(
            currentUser.GetRequiredUserId(principal),
            limit ?? 50,
            cancellationToken);
        return NoStore(Results.Ok(items.Select(item => new CoachConversationSummaryResponse(
            item.Id,
            item.CreatedAt,
            item.UpdatedAt,
            item.ContextVersion,
            CoachPreview(item.LastMessagePreview))).ToArray()));
    }

    private static async Task<IResult> GetCoachConversation(
        Guid id,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var conversation = await service.GetOwnedAsync(
            currentUser.GetRequiredUserId(principal), id, cancellationToken);
        if (conversation is null)
            return NoStore(Results.NotFound());
        return NoStore(Results.Ok(new CoachConversationResponse(
            conversation.Id,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.ContextVersion,
            conversation.Messages
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Select(message =>
                {
                    var analysis = message.Role == MessageRole.Assistant
                        ? ParseCoachAnalysis(message.Content)
                        : null;
                    return new CoachConversationMessageResponse(
                        message.Id,
                        message.Role.ToString().ToLowerInvariant(),
                        analysis?.Answer ?? message.Content,
                        analysis,
                        message.CreatedAt);
                })
                .ToArray())));
    }

    private static async Task<IResult> DeleteCoachConversation(
        Guid id,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(
            currentUser.GetRequiredUserId(principal), id, cancellationToken);
        return deleted ? NoStore(Results.NoContent()) : NoStore(Results.NotFound());
    }

    private static string? CoachPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;
        var analysis = ParseCoachAnalysis(content);
        return analysis?.Answer ?? (content.Length > 240 ? content[..240] : content);
    }

    private static CoachAnalysis? ParseCoachAnalysis(string content)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<CoachAnalysis>(
                content,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> AnalyzeActivity(
        AIFitnessCoach.Api.Contracts.Requests.ActivityAnalysisRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        HttpContext httpContext,
        [FromServices] AnalyzeActivityUseCase useCase,
        [FromServices] ICoachConsentStore consentStore,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question)
            || request.Question.Trim().Length > 1_000
            || request.Period is not ("last7Days" or "last30Days"))
        {
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = ["Question must contain 1 to 1000 characters."],
                ["period"] = ["Period must be last7Days or last30Days."]
            }));
        }

        try
        {
            if (!(await consentStore.GetAsync(
                    currentUser.GetRequiredUserId(principal), cancellationToken)).Granted)
                return NoStore(Results.Problem(
                    title: "Consent is required to use aggregated health context.",
                    statusCode: StatusCodes.Status403Forbidden));
            var response = await useCase.ExecuteAsync(
                currentUser.GetRequiredUserId(principal),
                new AIFitnessCoach.Application.UseCases.AI.ActivityAnalysisRequest(
                    request.ConversationId,
                    request.Question,
                    request.Period),
                cancellationToken);
            await analytics.RecordAsync(
                currentUser.GetRequiredUserId(principal),
                ProductEventNames.CoachQuery,
                new Dictionary<string, string>
                {
                    ["kind"] = "analysis",
                    ["period"] = request.Period,
                    ["question_hash"] = ProductEventNames.QuestionHash(request.Question)
                },
                cancellationToken);
            return NoStore(Results.Ok(ActivityAnalysisApiResponse.From(response, httpContext.TraceIdentifier)));
        }
        catch (ActivityAnalysisConversationNotFoundException)
        {
            return NoStore(Results.NotFound());
        }
        catch (ActivityAnalysisQuotaExceededException)
        {
            return NoStore(Results.Problem(
                title: "Daily AI analysis limit reached.",
                statusCode: StatusCodes.Status429TooManyRequests));
        }
        catch (ActivityAnalysisProviderException)
        {
            return NoStore(Results.Problem(
                title: "AI analysis is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }
        catch (UseCaseValidationException exception)
        {
            return NoStore(Results.ValidationProblem(exception.Errors));
        }
    }

    private static async Task<IResult> Coach(
        CoachRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachService service,
        [FromServices] ICoachConsentStore consentStore,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 4_000)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = ["Question must contain 1 to 4000 characters."]
            });

        try
        {
            if (!(await consentStore.GetAsync(
                    currentUser.GetRequiredUserId(principal), cancellationToken)).Granted)
                return NoStore(Results.Problem(
                    title: "Consent is required to use aggregated health context.",
                    statusCode: StatusCodes.Status403Forbidden));
            var response = await service.AskAsync(
                currentUser.GetRequiredUserId(principal),
                new CoachCommand(request.ConversationId, request.Question),
                cancellationToken);
            return NoStore(Results.Ok(new
            {
                response.ConversationId,
                message = new
                {
                    response.Answer.Id,
                    role = response.Answer.Role,
                    response.Answer.Content,
                    response.Answer.CreatedAt
                },
                response.InputTokens,
                response.OutputTokens,
                response.EstimatedCost
            }));
        }
        catch (AIConversationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (AIProviderException exception)
        {
            return Results.Problem(
                title: "AI coach is temporarily unavailable.",
                statusCode: exception.StatusCode is null
                    or StatusCodes.Status429TooManyRequests
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> Chat(
        AIChatRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] AIService service,
        [FromServices] ICoachConsentStore consentStore,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content) || request.Content.Trim().Length > 4_000)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["Message content must contain 1 to 4000 characters."]
            });

        try
        {
            if (!(await consentStore.GetAsync(
                    currentUser.GetRequiredUserId(principal), cancellationToken)).Granted)
                return NoStore(Results.Problem(
                    title: "Consent is required to use aggregated health context.",
                    statusCode: StatusCodes.Status403Forbidden));
            var response = await service.ChatAsync(
                currentUser.GetRequiredUserId(principal),
                new ChatCommand(request.ConversationId, request.Content),
                cancellationToken);
            return NoStore(Results.Ok(AIConversationResponseMapper.ToResponse(response)));
        }
        catch (AIConversationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (AIProviderException exception)
        {
            return Results.Problem(
                title: "AI service is temporarily unavailable.",
                statusCode: exception.StatusCode is null
                    or StatusCodes.Status429TooManyRequests
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = [exception.Message]
            });
        }
    }

        private static async Task<IResult> CreateCoachConversation(
            ClaimsPrincipal principal,
            ICurrentUser currentUser,
            [FromServices] CreateCoachConversationUseCase useCase,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            var userId = currentUser.GetRequiredUserId(principal);
            try
            {
                var conversationId = await useCase.ExecuteAsync(userId, cancellationToken);
                return NoStore(Results.Created(
                    $"/api/v1/coach/conversations/{conversationId}",
                    new { conversationId, createdAt = timeProvider.GetUtcNow() }));
            }
            catch (CoachActiveConversationLimitException)
            {
                return NoStore(Results.Problem(
                    title: "Coach conversation limit reached.",
                    statusCode: StatusCodes.Status409Conflict));
            }
        }

        private static async Task<IResult> SendCoachMessage(
            Guid id,
            AIFitnessCoach.Api.Contracts.Requests.CoachMessageRequest? request,
            ClaimsPrincipal principal,
            ICurrentUser currentUser,
            [FromServices] SendCoachMessageUseCase useCase,
            [FromServices] IProductEventRecorder analytics,
            TimeProvider timeProvider,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Message)
                || request.Message.Trim().Length > 1_000
                || request.ClientRequestId == Guid.Empty)
            {
                return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["message"] = ["Message must contain 1 to 1000 characters."],
                    ["clientRequestId"] = ["A non-empty clientRequestId is required."]
                }));
            }

            try
            {
                var userId = currentUser.GetRequiredUserId(principal);
                var response = await useCase.ExecuteAsync(
                    userId,
                    new AIFitnessCoach.Application.UseCases.AI.CoachMessageRequest(
                        id, request.ClientRequestId, request.Message, request.Period),
                    cancellationToken);
                // client_request_id lets reports ignore retries of the same question.
                await analytics.RecordAsync(
                    userId,
                    ProductEventNames.CoachQuery,
                    new Dictionary<string, string>
                    {
                        ["kind"] = "coach",
                        ["intent"] = response.Analysis.Intent,
                        ["client_request_id"] = request.ClientRequestId.ToString(),
                        ["question_hash"] = ProductEventNames.QuestionHash(request.Message)
                    },
                    cancellationToken);
                return NoStore(Results.Ok(response));
            }
            catch (CoachConsentRequiredException)
            {
                return NoStore(Results.Problem(
                    title: "Coach consent is required.",
                    statusCode: StatusCodes.Status403Forbidden));
            }
            catch (ActivityAnalysisConversationNotFoundException)
            {
                return NoStore(Results.NotFound());
            }
            catch (CoachConversationConflictException)
            {
                return NoStore(Results.Problem(
                    title: "This request is already being processed.",
                    statusCode: StatusCodes.Status409Conflict));
            }
            catch (CoachConversationLimitException)
            {
                return NoStore(Results.Problem(
                    title: "Coach conversation limit reached.",
                    statusCode: StatusCodes.Status409Conflict));
            }
            catch (ActivityAnalysisQuotaExceededException)
            {
                SetDailyRetryAfter(httpContext, timeProvider);
                return NoStore(Results.Problem(
                    title: "Daily AI Coach request limit reached.",
                    statusCode: StatusCodes.Status429TooManyRequests));
            }
            catch (CoachTokenQuotaExceededException)
            {
                SetDailyRetryAfter(httpContext, timeProvider);
                return NoStore(Results.Problem(
                    title: "Daily AI Coach token limit reached.",
                    statusCode: StatusCodes.Status429TooManyRequests));
            }
            catch (ActivityAnalysisProviderException)
            {
                return NoStore(Results.Problem(
                    title: "AI Coach is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable));
            }
            catch (UseCaseValidationException exception)
            {
                return NoStore(Results.ValidationProblem(exception.Errors));
            }
        }

        private static async Task<IResult> SubmitCoachFeedback(
            Guid id,
            AIFitnessCoach.Api.Contracts.Requests.CoachFeedbackRequest? request,
            ClaimsPrincipal principal,
            ICurrentUser currentUser,
            [FromServices] ICoachFeedbackStore feedbackStore,
            [FromServices] IProductEventRecorder analytics,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            if (request is null || request.MessageId == Guid.Empty
                || request.Rating is not ("Helpful" or "NotHelpful")
                || request.ReasonCode is not (null or "Helpful" or "NotHelpful" or "Incorrect"
                    or "Irrelevant" or "Unsafe" or "MissingContext" or "HardToUnderstand")
                || request.Comment is { Length: > 0 })
            {
                return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["feedback"] = ["Provide a valid rating and message id. Feedback comments are not accepted."]
                }));
            }
            var saved = await feedbackStore.AddAsync(
                currentUser.GetRequiredUserId(principal),
                id,
                request.MessageId,
                request.Rating,
                request.ReasonCode,
                request.Comment,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (saved)
            {
                // One event per rated answer (the store ignores a second rating of the same message).
                await analytics.RecordAsync(
                    currentUser.GetRequiredUserId(principal),
                    ProductEventNames.CoachFeedback,
                    new Dictionary<string, string>
                    {
                        ["rating"] = request.Rating == "Helpful" ? "positive" : "negative",
                        ["reason"] = request.ReasonCode ?? "none"
                    },
                    cancellationToken);
            }
            return saved ? NoStore(Results.NoContent()) : NoStore(Results.NotFound());
        }

        private static IResult SuggestedCoachQuestions() =>
            NoStore(Results.Ok(new[]
            {
                "¿Cómo ha ido mi semana?",
                "¿Cómo han cambiado mis pasos?",
                "¿Qué datos de sueño tengo registrados?",
                "¿Estoy siendo constante con mis entrenamientos?",
                "¿Qué tendencias generales muestran mis datos?"
            }));

        private static async Task<IResult> GetCoachConsent(
            ClaimsPrincipal principal,
            ICurrentUser currentUser,
            [FromServices] ICoachConsentStore consentStore,
            CancellationToken cancellationToken)
        {
            var consent = await consentStore.GetAsync(
                currentUser.GetRequiredUserId(principal), cancellationToken);
            return NoStore(Results.Ok(new { consent.Granted, consent.Version, consent.GrantedAt }));
        }

        private static async Task<IResult> SetCoachConsent(
            AIFitnessCoach.Api.Contracts.Requests.CoachConsentRequest? request,
            ClaimsPrincipal principal,
            ICurrentUser currentUser,
            [FromServices] ICoachConsentStore consentStore,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            if (request is null)
                return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["granted"] = ["A consent decision is required."]
                }));
            await consentStore.SetAsync(
                currentUser.GetRequiredUserId(principal),
                request.Granted,
                version: AIFitnessCoach.Application.UseCases.AI.CoachConsentVersion.Current,
                timeProvider.GetUtcNow(),
                cancellationToken);
            var consent = await consentStore.GetAsync(
                currentUser.GetRequiredUserId(principal), cancellationToken);
            return NoStore(Results.Ok(new { consent.Granted, consent.Version, consent.GrantedAt }));
        }

        private static async Task<IResult> RevokeAIConsent(
            ClaimsPrincipal principal,
            ICurrentUser currentUser,
            [FromServices] ICoachConsentStore consentStore,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            await consentStore.SetAsync(
                currentUser.GetRequiredUserId(principal),
                granted: false,
                version: AIFitnessCoach.Application.UseCases.AI.CoachConsentVersion.Current,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return NoStore(Results.NoContent());
        }
    private static async Task<IResult> ListConversations(
        int? limit,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var conversations = await service.ListAsync(
            currentUser.GetRequiredUserId(principal),
            limit ?? 50,
            cancellationToken);
        return NoStore(Results.Ok(conversations));
    }

    private static async Task<IResult> CreateAIConversation(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var conversation = await service.CreateAsync(currentUser.GetRequiredUserId(principal), cancellationToken);
        return NoStore(Results.Created($"/api/v1/ai/conversations/{conversation.Id}", new { conversationId = conversation.Id }));
    }

    private static async Task<IResult> GetAIUsageSummary(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var summary = await service.GetUsageSummaryAsync(
            currentUser.GetRequiredUserId(principal), now.AddDays(-30), now, cancellationToken);
        return NoStore(Results.Ok(summary));
    }

    private static async Task<IResult> GetConversation(
        Guid id,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var conversation = await service.GetOwnedAsync(
            currentUser.GetRequiredUserId(principal),
            id,
            cancellationToken);
        if (conversation is null)
            return Results.NotFound();
        var detail = new ConversationDetail(
            conversation.Id,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.ContextVersion,
            conversation.InputTokens,
            conversation.OutputTokens,
            conversation.EstimatedCost,
            conversation.Messages
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Select(message => new MessageView(
                    message.Id,
                    message.Role.ToString().ToLowerInvariant(),
                    message.Content,
                    message.CreatedAt))
                .ToArray());
        return NoStore(Results.Ok(AIConversationResponseMapper.ToResponse(detail)));
    }

    private static async Task<IResult> DeleteConversation(
        Guid id,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ConversationService service,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(
            currentUser.GetRequiredUserId(principal),
            id,
            cancellationToken);
        return deleted ? NoStore(Results.NoContent()) : Results.NotFound();
    }

    private static IResult NoStore(IResult result) =>
        new NoStoreResult(result);

    private static void SetDailyRetryAfter(HttpContext httpContext, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var secondsUntilReset = Math.Max(1, (int)Math.Ceiling(
            (now.Date.AddDays(1) - now).TotalSeconds));
        httpContext.Response.Headers.RetryAfter = secondsUntilReset.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class NoStoreResult(IResult inner) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            await inner.ExecuteAsync(httpContext);
        }
    }
}
