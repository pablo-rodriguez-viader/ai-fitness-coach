using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.RateLimiting;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIFitnessCoach.Api.Configuration;

public static class RateLimitPolicies
{
    public const string CoachMessage = "coach-message";
    public const string Authentication = "auth";
    public const string Telemetry = "telemetry";
    public const string Support = "support";
    public const string Account = "account";
}

public static class RequestTimeoutPolicies
{
    /// <summary>Endpoints that call the AI provider (60 s budget plus one retry inside it).</summary>
    public const string AI = "ai";
    /// <summary>Data export and other large reads.</summary>
    public const string Long = "long";
}

/// <summary>Rate limiting, request timeouts, health checks and proxy settings needed to run the API unattended.</summary>
public static class OperationalConfiguration
{
    private static readonly Meter Meter = new("AIFitnessCoach.Api");
    private static readonly Counter<long> CoachRateLimitRejections =
        Meter.CreateCounter<long>("coach_rate_limit_rejections_total");
    private static readonly Counter<long> RateLimitRejections =
        Meter.CreateCounter<long>("api_rate_limit_rejections_total");

    public static void AddOperationalServices(this IServiceCollection services, IConfiguration configuration)
    {
        var coachMessagesPerMinute = ReadLimit(configuration, "AI:CoachMessagesPerMinute", 10, 1, 60);
        var requestsPerMinute = ReadLimit(configuration, "RateLimiting:RequestsPerMinute", 300, 10, 10_000);
        var authRequestsPerMinute = ReadLimit(configuration, "RateLimiting:AuthRequestsPerMinute", 30, 1, 1_000);
        var telemetryRequestsPerMinute = ReadLimit(configuration, "RateLimiting:TelemetryRequestsPerMinute", 20, 1, 600);
        var supportRequestsPerMinute = ReadLimit(configuration, "RateLimiting:SupportRequestsPerMinute", 5, 1, 120);
        var accountRequestsPerMinute = ReadLimit(configuration, "RateLimiting:AccountRequestsPerMinute", 5, 1, 120);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                var route = context.HttpContext.GetEndpoint()?.DisplayName ?? "unmatched";
                RateLimitRejections.Add(1, new KeyValuePair<string, object?>("route", route));
                if (route.Contains("/coach/", StringComparison.Ordinal)
                    || route.Contains("/ai/", StringComparison.Ordinal))
                    CoachRateLimitRejections.Add(1);
                context.HttpContext.Response.Headers.RetryAfter = "60";
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting")
                    .LogWarning("Request rejected by rate limiting on {Route}.", route);
                return ValueTask.CompletedTask;
            };

            // Applies to every request; the named policies below add tighter limits on top.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                    return RateLimitPartition.GetNoLimiter("health");
                return FixedWindow(PartitionKey(context), requestsPerMinute);
            });
            options.AddPolicy(RateLimitPolicies.CoachMessage, context =>
                FixedWindow(PartitionKey(context), coachMessagesPerMinute));
            options.AddPolicy(RateLimitPolicies.Authentication, context =>
                FixedWindow("ip:" + ClientAddress(context), authRequestsPerMinute));
            options.AddPolicy(RateLimitPolicies.Telemetry, context =>
                FixedWindow(PartitionKey(context), telemetryRequestsPerMinute));
            options.AddPolicy(RateLimitPolicies.Support, context =>
                FixedWindow(PartitionKey(context), supportRequestsPerMinute));
            options.AddPolicy(RateLimitPolicies.Account, context =>
                FixedWindow(PartitionKey(context), accountRequestsPerMinute));
        });

        // A request that outlives its budget is cancelled and answered with 504, so a stuck
        // dependency cannot pile up connections and threads.
        var defaultTimeout = ReadLimit(configuration, "RequestTimeouts:DefaultSeconds", 30, 1, 300);
        var aiTimeout = ReadLimit(configuration, "RequestTimeouts:AISeconds", 90, 1, 300);
        var longTimeout = ReadLimit(configuration, "RequestTimeouts:LongSeconds", 120, 1, 600);
        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = TimeoutPolicy(defaultTimeout);
            options.AddPolicy(RequestTimeoutPolicies.AI, TimeoutPolicy(aiTimeout));
            options.AddPolicy(RequestTimeoutPolicies.Long, TimeoutPolicy(longTimeout));
        });

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            // Degraded (still 200) while the AI circuit is open: visible without failing readiness.
            .AddCheck<AIProviderHealthCheck>("ai_provider", tags: ["ready"]);

        if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                // Only enable behind a trusted load balancer that overwrites X-Forwarded-For.
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }
    }

    public static void UseOperationalMiddleware(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
            app.UseForwardedHeaders();
    }

    public static IEndpointRouteBuilder MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Kept for existing probes: process is up, no dependency checks.
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous()
            .DisableRateLimiting();
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false,
                ResponseWriter = WriteHealthResponse
            })
            .AllowAnonymous()
            .DisableRateLimiting();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = WriteHealthResponse
            })
            .AllowAnonymous()
            .DisableRateLimiting();
        return endpoints;
    }

    private static Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy TimeoutPolicy(int seconds) => new()
    {
        Timeout = TimeSpan.FromSeconds(seconds),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    };

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst("sub")?.Value is { Length: > 0 } userId
            ? "user:" + userId
            : "ip:" + ClientAddress(context);

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static int ReadLimit(IConfiguration configuration, string key, int fallback, int minimum, int maximum)
    {
        var value = configuration.GetValue<int?>(key) ?? fallback;
        if (value < minimum || value > maximum)
            throw new InvalidOperationException($"{key} must be between {minimum} and {maximum}.");
        return value;
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        // No exception details: probes are anonymous.
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1)
            })
        };
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}

/// <summary>
/// Reports the AI circuit breaker in /health/ready as Degraded (HTTP 200): the API keeps serving
/// everything else, but dashboards and alerts can see the AI outage.
/// </summary>
public sealed class AIProviderHealthCheck(
    AIFitnessCoach.Infrastructure.AI.AICircuitBreaker breaker,
    Microsoft.Extensions.Options.IOptions<AIFitnessCoach.AI.Configuration.AIOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
            return Task.FromResult(HealthCheckResult.Healthy("AI disabled."));
        return Task.FromResult(breaker.State switch
        {
            AIFitnessCoach.Infrastructure.AI.CircuitState.Closed => HealthCheckResult.Healthy(),
            AIFitnessCoach.Infrastructure.AI.CircuitState.HalfOpen =>
                HealthCheckResult.Degraded("AI provider circuit is half-open."),
            _ => HealthCheckResult.Degraded("AI provider circuit is open.")
        });
    }
}

public sealed class DatabaseHealthCheck(FitnessDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database check failed.", exception);
        }
    }
}
