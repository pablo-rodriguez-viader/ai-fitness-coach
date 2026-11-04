using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace AIFitnessCoach.Api.Configuration;

/// <summary>
/// Records low-cardinality API health metrics without user or request data and gives every
/// request a correlation id that is echoed in the <c>X-Correlation-ID</c> response header.
/// </summary>
public sealed partial class ApiRequestMetricsMiddleware(RequestDelegate next, ILogger<ApiRequestMetricsMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-ID";

    private static readonly Meter Meter = new("AIFitnessCoach.Api");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("api_requests_total");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("api_request_errors_total");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("api_request_duration_ms", "ms");

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationHeader, out var incoming)
            && IsValidCorrelationId(incoming.ToString()))
        {
            context.TraceIdentifier = incoming.ToString();
        }
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = context.TraceIdentifier;
            return Task.CompletedTask;
        });

        var started = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = context.TraceIdentifier
        });
        try
        {
            await next(context);
        }
        finally
        {
            var route = context.GetEndpoint()?.DisplayName ?? "unmatched";
            var method = context.Request.Method;
            var status = context.Response.StatusCode;
            var tags = new TagList { { "route", route }, { "method", method }, { "status_code", status } };
            Requests.Add(1, tags);
            if (status >= StatusCodes.Status500InternalServerError)
                Errors.Add(1, tags);
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Duration.Record(elapsedMilliseconds, tags);
            // Probes hit /health every few seconds; keep them out of the default log level.
            var level = context.Request.Path.StartsWithSegments("/health")
                ? LogLevel.Debug
                : status >= StatusCodes.Status500InternalServerError
                    ? LogLevel.Warning
                    : LogLevel.Information;
            logger.Log(
                level,
                "API request completed: {Route} {Method} {StatusCode} in {DurationMs} ms",
                route,
                method,
                status,
                Math.Round(elapsedMilliseconds, 1));
        }
    }

    internal static bool IsValidCorrelationId(string value) => CorrelationIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,64}$")]
    private static partial Regex CorrelationIdPattern();
}
