using System.Security.Claims;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Endpoints;

public sealed record ConsentRequest(bool? Granted, int? Version);

public sealed record ConsentResponse(string Kind, bool Granted, int Version, DateTimeOffset UpdatedAt);

/// <summary>
/// Explicit consents (GDPR art. 7 and 9): processing of synced health data and usage analytics.
/// The latest decision and the version of the text shown are kept as proof, are exported with
/// the account and deleted with it. AI consent keeps its own endpoint (/api/v1/ai/consent).
/// </summary>
public static class ConsentEndpoints
{
    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var consents = endpoints.MapGroup("/api/v1/consents").RequireAuthorization();
        consents.MapGet("", ListConsents);
        consents.MapPut("/{kind}", SetConsent);
        return endpoints;
    }

    private static async Task<IResult> ListConsents(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] IUserConsentStore store,
        CancellationToken cancellationToken)
    {
        var items = await store.ListAsync(currentUser.GetRequiredUserId(principal), cancellationToken);
        return Results.Ok(items.Select(ToResponse).ToList());
    }

    private static async Task<IResult> SetConsent(
        string kind,
        ConsentRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        [FromServices] IUserConsentStore store,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConsentKinds.All.Contains(kind))
            return Results.NotFound();
        if (request?.Granted is not { } granted || request.Version is not ({ } version and >= 1 and <= 1000))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["consent"] = ["granted and a text version (1-1000) are required."]
            });
        var userId = currentUser.GetRequiredUserId(principal);
        var consent = await store.SetAsync(userId, kind, granted, version, timeProvider.GetUtcNow(), cancellationToken);
        loggerFactory.CreateLogger("Consent").LogInformation(
            "Consent {Kind} set to {Granted} (text v{Version}) by user {UserId}.", kind, granted, version, userId);
        return Results.Ok(ToResponse(consent));
    }

    private static ConsentResponse ToResponse(UserConsent consent) =>
        new(consent.Kind, consent.Granted, consent.Version, consent.UpdatedAt);
}

/// <summary>
/// Rejects health uploads without an explicit health-data consent (403, code
/// "health_consent_required"). The app keeps its upload queue on 403 and resumes once the user
/// accepts. Privacy:RequireHealthConsent (default true).
/// </summary>
public sealed class HealthConsentFilter(IConfiguration configuration, IUserConsentStore store) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!(configuration.GetValue<bool?>("Privacy:RequireHealthConsent") ?? true))
            return await next(context);
        var userId = context.HttpContext.User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId))
            return await next(context);
        var consent = await store.GetAsync(userId, ConsentKinds.HealthData, context.HttpContext.RequestAborted);
        if (consent is { Granted: true })
            return await next(context);
        return Results.Problem(
            title: "Health data consent is required before syncing.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = "health_consent_required" });
    }
}
