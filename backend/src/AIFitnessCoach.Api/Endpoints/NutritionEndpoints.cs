using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Endpoints;

public static class NutritionEndpoints
{
    public static IEndpointRouteBuilder MapNutritionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var wellness = endpoints.MapGroup("/api/v1").RequireAuthorization();
        wellness.MapPost("/goals", CreateGoal);
        wellness.MapPost("/nutrition/goals", CreateGoal);
        wellness.MapGet("/goals", GetGoals);
        wellness.MapPost("/nutrition/profile", SetNutritionProfile);
        wellness.MapGet("/nutrition/profile", GetNutritionProfile);
        wellness.MapGet("/nutrition/targets", GetNutritionProfile);
        wellness.MapGet("/nutrition/progress", GetProgress);
        wellness.MapPost("/nutrition/log", AddNutritionLog);
        wellness.MapGet("/nutrition/summary", GetNutritionSummary);
        wellness.MapGet("/progress", GetProgress);
        return endpoints;
    }

    private static async Task<IResult> CreateGoal(
        AIFitnessCoach.Api.Contracts.Requests.GoalRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Type))
            return Validation("goal", "A supported goal type is required.");
        try
        {
            var userId = currentUser.GetRequiredUserId(principal);
            var goal = await service.CreateGoalAsync(
                userId,
                new AIFitnessCoach.AI.Contracts.GoalRequest(
                    request.Type,
                    request.TargetWeightKilograms,
                    request.TargetDate),
                cancellationToken);
            await analytics.RecordAsync(
                userId,
                ProductEventNames.GoalCreated,
                new Dictionary<string, string>
                {
                    ["type"] = request.Type,
                    ["has_target_weight"] = request.TargetWeightKilograms is null ? "false" : "true"
                },
                cancellationToken);
            return NoStore(Results.Ok(goal));
        }
        catch (ArgumentException exception)
        {
            return Validation("goal", exception.Message);
        }
    }

    private static async Task<IResult> GetGoals(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetGoalsAsync(currentUser.GetRequiredUserId(principal), cancellationToken)));

    private static async Task<IResult> SetNutritionProfile(
        AIFitnessCoach.Api.Contracts.Requests.NutritionProfileRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Sex) || string.IsNullOrWhiteSpace(request.ActivityLevel))
            return Validation("profile", "Sex and activity level are required.");
        try
        {
            var result = await service.SetNutritionProfileAsync(
                currentUser.GetRequiredUserId(principal),
                new AIFitnessCoach.AI.Contracts.NutritionProfileRequest(request.Sex, request.ActivityLevel),
                cancellationToken);
            return NoStore(Results.Ok(result));
        }
        catch (ArgumentException exception)
        {
            return Validation("profile", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Validation("profile", exception.Message);
        }
    }

    private static async Task<IResult> GetNutritionProfile(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return NoStore(Results.Ok(await service.GetNutritionProfileAsync(
                currentUser.GetRequiredUserId(principal),
                cancellationToken)));
        }
        catch (InvalidOperationException exception)
        {
            return Validation("profile", exception.Message);
        }
    }

    private static async Task<IResult> AddNutritionLog(
        AIFitnessCoach.Api.Contracts.Requests.NutritionLogRequest? request,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        [FromServices] IProductEventRecorder analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.MealType) || string.IsNullOrWhiteSpace(request.Name))
            return Validation("log", "Meal type and name are required.");
        try
        {
            var userId = currentUser.GetRequiredUserId(principal);
            var summary = await service.AddNutritionLogAsync(
                userId,
                new AIFitnessCoach.AI.Contracts.NutritionLogRequest(
                    request.MealType,
                    request.Name,
                    request.Calories,
                    request.ProteinGrams,
                    request.FatGrams,
                    request.CarbohydrateGrams,
                    request.Date),
                cancellationToken);
            await analytics.RecordAsync(
                userId,
                ProductEventNames.MealLogged,
                new Dictionary<string, string> { ["meal_type"] = request.MealType },
                cancellationToken);
            return NoStore(Results.Ok(summary));
        }
        catch (ArgumentException exception)
        {
            return Validation("log", exception.Message);
        }
    }

    private static async Task<IResult> GetNutritionSummary(
        DateOnly? date,
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        CancellationToken cancellationToken)
    {
        var requestedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return NoStore(Results.Ok(await service.GetNutritionSummaryAsync(
            currentUser.GetRequiredUserId(principal),
            requestedDate,
            cancellationToken)));
    }

    private static async Task<IResult> GetProgress(
        System.Security.Claims.ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] NutritionService service,
        CancellationToken cancellationToken) =>
        NoStore(Results.Ok(await service.GetProgressAsync(
            currentUser.GetRequiredUserId(principal),
            cancellationToken)));

    private static IResult Validation(string key, string message) =>
        NoStore(Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] }));

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
