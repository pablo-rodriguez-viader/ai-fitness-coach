using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.UseCases.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Endpoints;

public static class EngagementEndpoints
{
    public const string AnalyticsKeyHeader = AdminApiKey.Header;
    /// <summary>Shorter keys are rejected so the product report cannot be guessed.</summary>
    public const int MinimumAnalyticsKeyLength = AdminApiKey.MinimumLength;

    public static IEndpointRouteBuilder MapEngagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/engagement/summary", GetSummary).RequireAuthorization();
        // For the team running the beta, not for the app: protected by a shared secret.
        endpoints.MapGet("/api/v1/analytics/product", GetProductReport).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> GetSummary(
        int? utcOffsetMinutes,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] GetEngagementSummaryUseCase useCase,
        CancellationToken cancellationToken)
    {
        var summary = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            utcOffsetMinutes,
            cancellationToken);
        return new NoStoreResult(Results.Ok(summary));
    }

    private static async Task<IResult> GetProductReport(
        DateOnly? from,
        DateOnly? to,
        HttpContext httpContext,
        [FromServices] IConfiguration configuration,
        [FromServices] GetProductAnalyticsUseCase useCase,
        CancellationToken cancellationToken)
    {
        // Without a configured key the report does not exist, rather than being open.
        if (AdminApiKey.Check(httpContext, configuration) is { } failure)
            return failure;

        var report = await useCase.ExecuteAsync(from, to, cancellationToken);
        return new NoStoreResult(Results.Ok(report));
    }

    private sealed class NoStoreResult(IResult inner) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            await inner.ExecuteAsync(httpContext);
        }
    }
}
