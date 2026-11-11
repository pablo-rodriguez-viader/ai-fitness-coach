using System.Security.Claims;
using System.Text.Json;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Api.Contracts.Requests;
using AIFitnessCoach.Api.Contracts.Responses;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Application.UseCases.Profiles;
using AIFitnessCoach.Application.UseCases.Authentication;
using AIFitnessCoach.Application.Errors;
using Microsoft.Extensions.Logging;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/v1");
        root.MapPost("/auth/apple", SignInWithApple).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication);
        // Personal install (free Apple ID, no Sign in with Apple). 404 unless Auth:PersonalAccessCode is set.
        root.MapPost("/auth/personal", SignInWithPersonalCode).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication);
        root.MapPost("/auth/refresh", RefreshSession).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication);
        root.MapPost("/auth/revoke", RevokeSession).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        var api = root.MapGroup("").RequireAuthorization();

        api.MapPost("/auth/logout", Logout);
        api.MapGet("/profile/me", GetUserProfile);
        api.MapPut("/profile/me", CreateUserProfile);
        api.MapGet("/health-metrics", GetHealthMetrics);
        api.MapGet("/health-metrics/latest", GetLatestHealthMetrics);
        api.MapGet("/health-summary", GetHealthSummary);
        // Uploads need the explicit health-data consent (GDPR art. 9).
        api.MapPost("/health-metrics", CreateHealthMetric).AddEndpointFilter<HealthConsentFilter>();
        api.MapPost("/health-metrics/batch", SyncHealthMetrics).AddEndpointFilter<HealthConsentFilter>();
        api.MapPost("/health-metrics/sync", SyncHealthMetrics).AddEndpointFilter<HealthConsentFilter>();
        endpoints.MapAIEndpoints();
        endpoints.MapWorkoutEndpoints();
        endpoints.MapNutritionEndpoints();
        endpoints.MapTelemetryEndpoints();
        endpoints.MapEngagementEndpoints();
        endpoints.MapReleaseEndpoints();
        endpoints.MapConsentEndpoints();
        endpoints.MapShortcutEndpoints();
        return endpoints;
    }

    private static async Task<IResult> SignInWithApple(
        AppleSignInRequest? request,
        AuthenticationService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return InvalidBody("appleSignIn");
        var pair = await service.SignInWithAppleAsync(
            new AppleSignInCommand(
                request.IdentityToken ?? string.Empty,
                request.Nonce ?? string.Empty,
                request.DisplayName),
            cancellationToken);
        return NoStore(Results.Ok(ToResponse(pair)));
    }

    private sealed record PersonalSignInRequest(string? Code);

    private static async Task<IResult> SignInWithPersonalCode(
        PersonalSignInRequest? request,
        AuthenticationService service,
        CancellationToken cancellationToken)
    {
        if (!service.IsPersonalSignInEnabled)
            return Results.NotFound();
        if (request is null)
            return InvalidBody("code");
        var pair = await service.SignInWithPersonalCodeAsync(request.Code ?? string.Empty, cancellationToken);
        return NoStore(Results.Ok(ToResponse(pair)));
    }

    private static async Task<IResult> RefreshSession(
        RefreshTokenRequest? request,
        AuthenticationService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return InvalidBody("refreshToken");
        var pair = await service.RefreshAsync(request.RefreshToken ?? string.Empty, cancellationToken);
        return NoStore(Results.Ok(ToResponse(pair)));
    }

    private static async Task<IResult> RevokeSession(
        RefreshTokenRequest? request,
        AuthenticationService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return InvalidBody("refreshToken");
        await service.RevokeAsync(request.RefreshToken ?? string.Empty, cancellationToken);
        return NoStore(Results.NoContent());
    }

    private static async Task<IResult> Logout(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        AuthenticationService service,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId))
            throw new UnauthorizedAccessException("A validated session claim is required.");
        await service.RevokeSessionAsync(
            currentUser.GetRequiredUserId(principal),
            sessionId,
            cancellationToken);
        return NoStore(Results.NoContent());
    }

    private static async Task<IResult> GetUserProfile(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        GetUserProfileUseCase useCase,
        CancellationToken cancellationToken)
    {
        var profile = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            cancellationToken);
        return profile is null
            ? Results.NotFound()
            : Results.Ok(new UserProfileResponse(
                profile.UserId,
                profile.DisplayName,
                profile.Age,
                profile.HeightCentimeters,
                profile.Goal,
                profile.CreatedAt,
                profile.UpdatedAt));
    }

    private static async Task<IResult> CreateUserProfile(
        UserProfileRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        CreateUserProfileUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["profile"] = ["A profile request body is required."]
            });
        }

        var profile = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            new CreateUserProfileCommand(
                request.DisplayName,
                request.Age,
                request.HeightCentimeters,
                request.Goal),
            cancellationToken);
        return Results.Ok(new UserProfileResponse(
            profile.UserId,
            profile.DisplayName,
            profile.Age,
            profile.HeightCentimeters,
            profile.Goal,
            profile.CreatedAt,
            profile.UpdatedAt));
    }

    private static async Task<IResult> GetHealthMetrics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? cursor,
        int? pageSize,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        GetHealthMetricsUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!CursorCodec.TryDecode(cursor, out var decodedCursor))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cursor"] = ["Cursor is malformed."]
            });
        }

        var page = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            from,
            to,
            decodedCursor,
            pageSize ?? 100,
            cancellationToken);
        return NoStore(Results.Ok(new HealthMetricsPageResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.NextCursor is null ? null : CursorCodec.Encode(page.NextCursor),
            page.From,
            page.To)));
    }

    private static async Task<IResult> CreateHealthMetric(
        HealthMetricRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        CreateHealthMetricUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metric"] = ["A health metric request body is required."]
            });
        }

        var metric = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            new CreateHealthMetricCommand(
                request.Metric,
                request.Value,
                request.Unit,
                request.ObservedAt,
                request.Source,
                request.ExternalId ?? request.SourceRecordId,
                request.DeviceId),
            cancellationToken);

        return Results.Created(
            $"/api/v1/health-metrics/{metric.Id}",
            ToResponse(metric));
    }

    private static async Task<IResult> SyncHealthMetrics(
        SyncHealthMetricsRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        SyncHealthMetricsUseCase useCase,
        DeleteHealthMetricsUseCase deleteUseCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return InvalidBody("metrics");

        var userId = currentUser.GetRequiredUserId(principal);
        var logger = loggerFactory.CreateLogger("HealthSync");
        if (!string.IsNullOrWhiteSpace(request.UserId)
            && !string.Equals(request.UserId, userId, StringComparison.Ordinal))
        {
            HealthSyncTelemetry.RecordError();
            return Results.Forbid();
        }

        if ((request.Metrics?.Count ?? 0) == 0 && (request.DeletedMetrics?.Count ?? 0) == 0)
            return InvalidBody("metrics");
        if ((request.Metrics?.Count ?? 0) + (request.DeletedMetrics?.Count ?? 0) > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metrics"] = ["A sync batch cannot contain more than 500 changes."]
            });

        var result = new HealthMetricBatchResult(0, 0, 0);
        if (request.Metrics is { Count: > 0 } metrics)
        {
            try
            {
                result = await useCase.ExecuteAsync(
                    userId,
                    new SyncHealthMetricsCommand(metrics.Select(metric =>
                        new CreateHealthMetricCommand(
                            metric.Metric,
                            metric.Value,
                            metric.Unit,
                            metric.ObservedAt,
                            metric.Source,
                            metric.ExternalId ?? metric.SourceRecordId,
                            metric.DeviceId)).ToArray()),
                    cancellationToken,
                    skipInvalidItems: true);
            }
            catch (UseCaseValidationException exception)
            {
                HealthSyncTelemetry.RecordError();
                logger.LogWarning(
                    "Health sync batch validation failed for {ErrorCount} fields.",
                    exception.Errors.Count);
                throw;
            }
        }
        var deletedCount = 0;
        if (request.DeletedMetrics is { Count: > 0 } deletedMetrics)
        {
            try
            {
                foreach (var group in deletedMetrics.GroupBy(
                             item => item.Source ?? string.Empty,
                             StringComparer.Ordinal))
                {
                    deletedCount += await deleteUseCase.ExecuteAsync(
                        userId,
                        new DeleteHealthMetricsCommand(
                            group.Key,
                            group.Select(item => item.ExternalId ?? string.Empty).ToArray(),
                            // Deletions are keyed by source + external id; the device id is only
                            // required as provenance, so a mixed batch must not be rejected.
                            group.Select(item => item.DeviceId)
                                .FirstOrDefault(deviceId => !string.IsNullOrWhiteSpace(deviceId))),
                        cancellationToken);
                }
            }
            catch (UseCaseValidationException exception)
            {
                HealthSyncTelemetry.RecordError();
                logger.LogWarning(
                    "Health sync deletion validation failed for {ErrorCount} fields.",
                    exception.Errors.Count);
                throw;
            }
        }
        var receivedCount = (request.Metrics?.Count ?? 0) + (request.DeletedMetrics?.Count ?? 0);
        HealthSyncTelemetry.RecordSuccess(receivedCount, result, deletedCount);
        if (result.RejectedCount > 0)
        {
            HealthSyncTelemetry.RecordRejected(result.RejectedCount);
            logger.LogWarning(
                "Health sync batch skipped {RejectedCount} invalid samples.",
                result.RejectedCount);
        }
        logger.LogInformation(
            "Health sync batch completed: received {ReceivedCount}, inserted {InsertedCount}, updated {UpdatedCount}, deduplicated {DuplicateCount}, deleted {DeletedCount}, rejected {RejectedCount}.",
            receivedCount,
            result.InsertedCount,
            result.UpdatedCount,
            result.DuplicateCount,
            deletedCount,
            result.RejectedCount);
        return NoStore(Results.Ok(new HealthMetricBatchResponse(
            receivedCount,
            result.InsertedCount,
            result.UpdatedCount,
            result.DuplicateCount,
            deletedCount,
            result.RejectedCount)));
    }

    private static HealthMetricResponse ToResponse(HealthMetric metric) =>
        new(
            metric.Id,
            metric.UserId,
            metric.Metric,
            metric.Value,
            metric.Unit,
            metric.ObservedAt,
            metric.CreatedAt,
            metric.Source,
            metric.ExternalId,
            metric.DeviceId,
            metric.SyncVersion,
            metric.UpdatedAt);

    private static async Task<IResult> GetLatestHealthMetrics(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        GetLatestHealthMetricsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var metrics = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            cancellationToken);
        return NoStore(Results.Ok(metrics.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> GetHealthSummary(
        DateTimeOffset? from,
        DateTimeOffset? to,
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        GetHealthSummaryUseCase useCase,
        CancellationToken cancellationToken)
    {
        var summary = await useCase.ExecuteAsync(
            currentUser.GetRequiredUserId(principal),
            from,
            to,
            cancellationToken);
        return NoStore(Results.Ok(new HealthMetricSummaryResponse(
            summary.Steps,
            summary.WeightKilograms,
            summary.ActiveEnergyKilocalories,
            summary.TotalEnergyKilocalories,
            summary.AverageHeartRateBeatsPerMinute,
            summary.SleepSeconds)));
    }

    private static AuthenticationResponse ToResponse(AuthenticationTokenPair pair) =>
        new(pair.UserId, pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt);

    private static IResult InvalidBody(string field) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = ["A request body is required."]
        });

    private static IResult NoStore(IResult result)
    {
        return new HeaderResult(result);
    }

    private sealed class HeaderResult(IResult inner) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            await inner.ExecuteAsync(httpContext);
        }
    }

    private static class CursorCodec
    {
        public static string Encode(HealthMetricCursor cursor)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static bool TryDecode(string? value, out HealthMetricCursor? cursor)
        {
            cursor = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
            if (value.Length > 256)
            {
                return false;
            }

            try
            {
                var base64 = value.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                var bytes = Convert.FromBase64String(base64);
                cursor = JsonSerializer.Deserialize<HealthMetricCursor>(bytes);
                return cursor is not null && cursor.Id != Guid.Empty;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
