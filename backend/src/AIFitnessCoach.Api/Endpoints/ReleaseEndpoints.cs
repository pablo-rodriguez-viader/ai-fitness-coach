using System.Security.Claims;
using System.Text.Json;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.UseCases.Account;
using AIFitnessCoach.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AIFitnessCoach.Api.Endpoints;

public sealed record DeleteAccountRequest(string? AuthorizationCode);

public sealed record SupportReportRequest(
    string? Kind,
    string? Category,
    string? Message,
    int? Rating,
    string? ContactEmail,
    string? AppVersion,
    string? BuildNumber,
    string? OsVersion,
    string? DeviceModel,
    IReadOnlyDictionary<string, string>? Diagnostics);

public sealed record SupportReportCreatedResponse(Guid Id, string Kind, DateTimeOffset CreatedAt);

public sealed record SupportReportResponse(
    Guid Id,
    string UserId,
    string Kind,
    string Category,
    string Message,
    int? Rating,
    string? ContactEmail,
    string? AppVersion,
    string? BuildNumber,
    string? OsVersion,
    string? DeviceModel,
    IReadOnlyDictionary<string, string> Diagnostics,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// What the App Store and the private beta need around the product: remote configuration and
/// feature flags, data export, account deletion, and the Feedback Center / bug report inbox.
/// </summary>
public static class ReleaseEndpoints
{
    public static IEndpointRouteBuilder MapReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Read before sign-in (support links on the login screen), so it is anonymous.
        endpoints.MapGet("/api/v1/app-config", GetAppConfig).AllowAnonymous();

        var account = endpoints.MapGroup("/api/v1/account").RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Account);
        account.MapGet("/export", ExportAccount).WithRequestTimeout(RequestTimeoutPolicies.Long);
        account.MapPost("/delete", DeleteAccount);
        account.MapDelete("", DeleteAccount);
        // GDPR erasure of one category without closing the account.
        account.MapDelete("/ai-data", DeleteAIData);
        account.MapDelete("/health-data", DeleteHealthData);

        endpoints.MapPost("/api/v1/support/reports", SubmitReport)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Support);
        // For the team running the beta, protected by the admin key.
        endpoints.MapGet("/api/v1/support/reports", ListReports).AllowAnonymous();
        return endpoints;
    }

    private static IResult GetAppConfig(
        HttpContext httpContext,
        [FromServices] IConfiguration configuration,
        [FromServices] IOptions<AIOptions> aiOptions)
    {
        // Short cache: a flag turned off reaches testers within minutes.
        httpContext.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok(ReleaseConfiguration.Build(configuration, aiOptions));
    }

    private static async Task<IResult> ExportAccount(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        [FromServices] ExportAccountDataUseCase useCase,
        CancellationToken cancellationToken)
    {
        var export = await useCase.ExecuteAsync(currentUser.GetRequiredUserId(principal), cancellationToken);
        if (export is null)
            return Results.NotFound();
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.ContentDisposition =
            $"attachment; filename=\"aifitnesscoach-export-{export.GeneratedAt:yyyyMMdd}.json\"";
        return Results.Ok(export);
    }

    private static async Task<IResult> DeleteAccount(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        [FromServices] DeleteAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = await ReadOptionalBodyAsync<DeleteAccountRequest>(httpContext, cancellationToken);
        var userId = currentUser.GetRequiredUserId(principal);
        var result = await useCase.ExecuteAsync(userId, request?.AuthorizationCode, cancellationToken);
        loggerFactory.CreateLogger("Account").LogWarning(
            "Account {UserId} deletion requested: deleted={Deleted}, apple revocation={AppleRevocation}.",
            userId,
            result.Deleted,
            result.AppleRevocation);
        httpContext.Response.Headers.CacheControl = "no-store";
        return result.Deleted ? Results.Ok(result) : Results.NotFound();
    }

    /// <summary>Withdraws AI consent and deletes everything sent to or produced by the AI provider.</summary>
    private static async Task<IResult> DeleteAIData(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        [FromServices] IAccountDataStore store,
        [FromServices] AIFitnessCoach.Application.UseCases.AI.ICoachConsentStore consentStore,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetRequiredUserId(principal);
        // Consent first: no new AI request can start while the data is being deleted.
        await consentStore.SetAsync(userId, granted: false, version: AIFitnessCoach.Application.UseCases.AI.CoachConsentVersion.Current, timeProvider.GetUtcNow(), cancellationToken);
        var conversations = await store.DeleteAIDataAsync(userId, cancellationToken);
        loggerFactory.CreateLogger("Account").LogInformation(
            "AI data deleted for user {UserId}: {Conversations} conversations.", userId, conversations);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { conversationsDeleted = conversations, consentGranted = false });
    }

    /// <summary>Deletes every synced Apple Health sample. The app also stops syncing.</summary>
    private static async Task<IResult> DeleteHealthData(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        [FromServices] IAccountDataStore store,
        [FromServices] IUserConsentStore consentStore,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetRequiredUserId(principal);
        // Withdraw first so an upload already in flight is rejected instead of re-adding data.
        await consentStore.SetAsync(userId, ConsentKinds.HealthData, granted: false, version: 1,
            timeProvider.GetUtcNow(), cancellationToken);
        var samples = await store.DeleteHealthDataAsync(userId, cancellationToken);
        loggerFactory.CreateLogger("Account").LogInformation(
            "Health data deleted for user {UserId}: {Samples} samples.", userId, samples);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { samplesDeleted = samples });
    }

    private static async Task<IResult> SubmitReport(
        SupportReportRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        [FromServices] SubmitSupportReportUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["report"] = ["A report body is required."]
            });
        var userId = currentUser.GetRequiredUserId(principal);
        var report = await useCase.ExecuteAsync(
            userId,
            new SubmitSupportReportCommand(
                request.Kind ?? string.Empty,
                request.Category,
                request.Message,
                request.Rating,
                request.ContactEmail,
                request.AppVersion,
                request.BuildNumber,
                request.OsVersion,
                request.DeviceModel,
                request.Diagnostics),
            cancellationToken);
        // The message itself stays in the database; logs only carry metadata.
        loggerFactory.CreateLogger("Support").Log(
            report.Kind == SupportReportKinds.Bug ? LogLevel.Warning : LogLevel.Information,
            "Support report {ReportId} kind={Kind} category={Category} from user {UserId} app {AppVersion} ({Build}).",
            report.Id,
            report.Kind,
            report.Category,
            userId,
            report.AppVersion,
            report.BuildNumber);
        return Results.Created(
            $"/api/v1/support/reports/{report.Id}",
            new SupportReportCreatedResponse(report.Id, report.Kind, report.CreatedAt));
    }

    private static async Task<IResult> ListReports(
        string? kind,
        DateTimeOffset? since,
        int? limit,
        HttpContext httpContext,
        [FromServices] IConfiguration configuration,
        [FromServices] ISupportReportRepository repository,
        CancellationToken cancellationToken)
    {
        if (AdminApiKey.Check(httpContext, configuration) is { } failure)
            return failure;
        var reports = await repository.ListAsync(kind, since, limit ?? 100, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(reports.Select(report => new SupportReportResponse(
            report.Id,
            report.UserId,
            report.Kind,
            report.Category,
            report.Message,
            report.Rating,
            report.ContactEmail,
            report.AppVersion,
            report.BuildNumber,
            report.OsVersion,
            report.DeviceModel,
            JsonSerializer.Deserialize<Dictionary<string, string>>(report.DiagnosticsJson) ?? [],
            report.Status,
            report.CreatedAt)).ToList());
    }

    /// <summary>DELETE requests usually have no body; POST /delete may carry the Apple code.</summary>
    private static async Task<T?> ReadOptionalBodyAsync<T>(HttpContext httpContext, CancellationToken cancellationToken)
        where T : class
    {
        if (httpContext.Request.ContentLength is null or 0 && !httpContext.Request.Headers.ContainsKey("Transfer-Encoding"))
            return null;
        if (!httpContext.Request.HasJsonContentType())
            throw new BadHttpRequestException("A JSON body is required.");
        try
        {
            return await httpContext.Request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new BadHttpRequestException("The request body is not valid JSON.");
        }
    }
}
