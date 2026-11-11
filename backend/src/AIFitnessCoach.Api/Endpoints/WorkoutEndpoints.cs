using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.AI.UseCases;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Api.Contracts.Requests;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Endpoints;

public static class WorkoutEndpoints
{
    public static IEndpointRouteBuilder MapWorkoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var coach = endpoints.MapGroup("/api/v1/coach").RequireAuthorization();
        coach.MapPost("/workout-plan", GeneratePlan).RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        coach.MapGet("/workout-plan", GetLatestPlan);
        coach.MapGet("/workout-history", GetHistory);
        coach.MapPost("/workout-feedback", RecordFeedback);
        coach.MapGet("/recommendations", GetRecommendations);
        coach.MapGet("/insights", GetInsights);
        coach.MapGet("/progress-analysis", GetProgressAnalysis);
        coach.MapGet("/adherence", GetAdherence);
        coach.MapGet("/recommendation-history", GetRecommendationHistory);
        coach.MapPost("/recommendation-feedback", RecordRecommendationFeedback);
        coach.MapGet("/behavior", GetInsights);
        coach.MapGet("/preferences", GetPreferences);

        var plans = endpoints.MapGroup("/api/v1/workout-plans").RequireAuthorization();
        plans.MapPost("/generate", GeneratePlan).RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        plans.MapGet("", ListPlans);
        plans.MapGet("/{id:guid}", GetPlan);
        plans.MapPost("/{id:guid}/feedback", RecordPlanFeedback);
        plans.MapPost("/{id:guid}/complete", CompleteDay);

        var workouts = endpoints.MapGroup("/api/v1/workouts").RequireAuthorization();
        workouts.MapPost("/generate", GeneratePlan).RequireRateLimiting("coach-message").WithRequestTimeout(RequestTimeoutPolicies.AI);
        workouts.MapGet("/current", GetLatestPlan);
        workouts.MapGet("/history", GetHistory);
        workouts.MapPost("/complete", CompleteWorkout);
        workouts.MapPost("/feedback", RecordFeedback);
        return endpoints;
    }

    private static async Task<IResult> GeneratePlan(
        AIFitnessCoach.Api.Contracts.Requests.WorkoutPlanRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] GenerateWorkoutPlanUseCase useCase,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Goal)
            || !WorkoutGoal.Supported.Contains(request.Goal) || request.Days is < 1 or > 7
            || request.AvailableMinutes is < 10 or > 180
            || request.Equipment?.Count > 8
            || request.Needs?.Length > WorkoutService.MaximumNeedsLength)
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["A supported goal and a duration from 1 to 7 days are required."]
            }));
        try
        {
            var userId = currentUser.GetRequiredUserId(principal);
            var plan = await useCase.ExecuteAsync(
                userId,
                new AIFitnessCoach.AI.Contracts.WorkoutPlanRequest(
                    request.Goal, request.Days, request.AvailableMinutes, request.Equipment,
                    string.IsNullOrWhiteSpace(request.Needs) ? null : request.Needs.Trim()),
                cancellationToken);
            await analytics.RecordAsync(
                userId,
                ProductEventNames.WorkoutPlanGenerated,
                new Dictionary<string, string> { ["goal"] = request.Goal, ["days"] = request.Days.ToString() },
                cancellationToken);
            return NoStore(Results.Ok(plan));
        }
        catch (AIProviderException exception)
        {
            return NoStore(Results.Problem(
                title: "Workout generation is temporarily unavailable.",
                statusCode: exception.StatusCode is null or StatusCodes.Status429TooManyRequests
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway));
        }
        catch (ArgumentException exception)
        {
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> ListPlans(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetPlansAsync(
            currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> GetPlan(
        Guid id,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        CancellationToken cancellationToken)
    {
        var plan = await service.GetPlanAsync(currentUser.GetRequiredUserId(principal), id, cancellationToken);
        return NoStore(plan is null ? Results.NotFound() : Results.Ok(plan));
    }

    private static async Task<IResult> RecordPlanFeedback(
        Guid id,
        AIFitnessCoach.Api.Contracts.Requests.WorkoutFeedbackRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || request.WorkoutDayId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Difficulty) || request.Comment?.Length > 500)
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["feedback"] = ["A workout day and one of the five supported difficulty levels are required."]
            }));
        try
        {
            var userId = currentUser.GetRequiredUserId(principal);
            var result = await service.RecordPlanFeedbackAsync(
                userId, id,
                new AIFitnessCoach.AI.Contracts.WorkoutFeedbackRequest(
                    request.WorkoutDayId, request.Completed, request.Difficulty, request.Comment),
                cancellationToken);
            await RecordFeedbackEventsAsync(analytics, userId, request.WorkoutDayId, request.Completed, request.Difficulty, cancellationToken);
            return NoStore(Results.Ok(result));
        }
        catch (WorkoutNotFoundException)
        {
            return NoStore(Results.NotFound());
        }
        catch (ArgumentException exception)
        {
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]> { ["feedback"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> CompleteDay(
        Guid id,
        WorkoutCompletionRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || request.WorkoutDayId == Guid.Empty)
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["workoutDayId"] = ["A workout day is required."]
            }));
        try
        {
            var userId = currentUser.GetRequiredUserId(principal);
            var completion = await service.CompleteDayAsync(
                userId, id, request.WorkoutDayId,
                request.Completed, cancellationToken);
            if (completion.Completed)
                await RecordCompletionAsync(analytics, userId, request.WorkoutDayId, cancellationToken);
            return NoStore(Results.Ok(new { completion.Id, completion.WorkoutDayId, completion.Completed, completion.RecordedAt }));
        }
        catch (WorkoutNotFoundException)
        {
            return NoStore(Results.NotFound());
        }
    }

    private static async Task<IResult> CompleteWorkout(
        WorkoutCompletionRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || request.WorkoutDayId == Guid.Empty)
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["workoutDayId"] = ["A workout day is required."]
            }));
        var userId = currentUser.GetRequiredUserId(principal);
        var plan = await service.GetPlanForDayAsync(userId, request.WorkoutDayId, cancellationToken);
        if (plan is null)
            return NoStore(Results.NotFound());
        try
        {
            var completion = await service.CompleteDayAsync(
                userId, plan.Id, request.WorkoutDayId, request.Completed, cancellationToken);
            if (completion.Completed)
                await RecordCompletionAsync(analytics, userId, request.WorkoutDayId, cancellationToken);
            return NoStore(Results.Ok(new { completion.Id, completion.WorkoutDayId, completion.Completed, completion.RecordedAt }));
        }
        catch (WorkoutNotFoundException)
        {
            return NoStore(Results.NotFound());
        }
    }

    private static async Task<IResult> GetLatestPlan(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        CancellationToken cancellationToken)
    {
        var plan = await service.GetLatestPlanAsync(currentUser.GetRequiredUserId(principal), cancellationToken);
        return NoStore(plan is null ? Results.NotFound() : Results.Ok(plan));
    }

    private static async Task<IResult> GetHistory(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetHistoryAsync(currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> RecordFeedback(
        AIFitnessCoach.Api.Contracts.Requests.WorkoutFeedbackRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] AdaptWorkoutPlanUseCase useCase,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || request.WorkoutDayId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Difficulty) || request.Comment?.Length > 500)
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["feedback"] = ["A workout day, difficulty, completion state, and optional comment are required."]
            }));
        try
        {
            var userId = currentUser.GetRequiredUserId(principal);
            var result = await useCase.ExecuteAsync(
                userId,
                new AIFitnessCoach.AI.Contracts.WorkoutFeedbackRequest(
                    request.WorkoutDayId,
                    request.Completed,
                    request.Difficulty,
                    request.Comment),
                cancellationToken);
            await RecordFeedbackEventsAsync(analytics, userId, request.WorkoutDayId, request.Completed, request.Difficulty, cancellationToken);
            return NoStore(Results.Ok(result));
        }
        catch (WorkoutNotFoundException)
        {
            return NoStore(Results.NotFound());
        }
        catch (ArgumentException exception)
        {
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]> { ["feedback"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> GetRecommendations(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] WorkoutService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetRecommendationsAsync(currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> GetInsights(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachInsightsService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetInsightsAsync(
            currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> GetProgressAnalysis(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachInsightsService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetProgressAnalysisAsync(
            currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> GetAdherence(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachInsightsService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetAdherenceAsync(
            currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> GetPreferences(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachInsightsService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetInsightsAsync(
            currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> GetRecommendationHistory(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachInsightsService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetRecommendationHistoryAsync(
            currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> RecordRecommendationFeedback(
        RecommendationFeedback? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] CoachInsightsService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["feedback"] = ["A recommendation ID and score from 1 to 5 are required."]
            }));
        try
        {
            await service.RecordRecommendationFeedbackAsync(
                currentUser.GetRequiredUserId(principal),
                request,
                cancellationToken);
            return NoStore(Results.Ok(new { accepted = true }));
        }
        catch (RecommendationNotFoundException)
        {
            return NoStore(Results.NotFound());
        }
        catch (ArgumentException exception)
        {
            return NoStore(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["feedback"] = [exception.Message]
            }));
        }
    }

    private static async Task RecordFeedbackEventsAsync(
        IProductEventRecorder analytics,
        string userId,
        Guid workoutDayId,
        bool completed,
        string difficulty,
        CancellationToken cancellationToken)
    {
        await analytics.RecordAsync(
            userId,
            ProductEventNames.WorkoutFeedbackSubmitted,
            new Dictionary<string, string>
            {
                ["workout_day_id"] = workoutDayId.ToString(),
                ["completed"] = completed ? "true" : "false",
                ["difficulty"] = difficulty
            },
            cancellationToken);
        if (completed)
            await RecordCompletionAsync(analytics, userId, workoutDayId, cancellationToken);
    }

    private static Task RecordCompletionAsync(
        IProductEventRecorder analytics,
        string userId,
        Guid workoutDayId,
        CancellationToken cancellationToken) =>
        analytics.RecordAsync(
            userId,
            ProductEventNames.WorkoutCompleted,
            new Dictionary<string, string> { ["workout_day_id"] = workoutDayId.ToString() },
            cancellationToken);

    private static IResult NoStore(IResult result) => new NoStoreResult(result);

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
