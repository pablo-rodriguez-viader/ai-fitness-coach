using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AIFitnessCoach.Api.Configuration;

/// <summary>
/// Sends the API's meters (requests, errors, sync, AI, client crashes, support, account), traces
/// and logs to an OpenTelemetry collector (Grafana, Datadog, Honeycomb, New Relic...). Off unless
/// OpenTelemetry:OtlpEndpoint (or the standard OTEL_EXPORTER_OTLP_ENDPOINT) is set, so local runs
/// and tests keep stdout logs only.
/// </summary>
public static class ObservabilityConfiguration
{
    /// <summary>Every meter in the solution is named AIFitnessCoach.*.</summary>
    public const string MeterPrefix = "AIFitnessCoach.*";

    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(Endpoint(configuration));

    public static void AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = Endpoint(configuration);
        if (string.IsNullOrWhiteSpace(endpoint))
            return;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            throw new InvalidOperationException("OpenTelemetry:OtlpEndpoint must be an absolute URL.");
        var protocol = string.Equals(configuration["OpenTelemetry:Protocol"], "http", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
        var environment = configuration["Release:Environment"] ?? "beta";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: configuration["OpenTelemetry:ServiceName"] ?? "aifitnesscoach-api",
                    serviceVersion: typeof(ObservabilityConfiguration).Assembly.GetName().Version?.ToString())
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environment)]))
            .WithMetrics(metrics => metrics
                .AddMeter(MeterPrefix)
                .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http"))
            .WithTracing(tracing => tracing
                .AddSource("Microsoft.AspNetCore", "System.Net.Http")
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(
                    Math.Clamp(configuration.GetValue<double?>("OpenTelemetry:TraceSampleRatio") ?? 0.2, 0, 1)))))
            .WithLogging()
            .UseOtlpExporter(protocol, endpointUri);
    }

    private static string? Endpoint(IConfiguration configuration) =>
        configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 } configured
            ? configured
            : configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
}
