using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIFitnessCoach.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

/// <summary>Fast checks that a freshly deployed API is usable before testers touch it.</summary>
public sealed class BetaSmokeTests
{
    [Fact]
    public async Task ApiBootsWithProductionCompositionAndAnswersHealthProbes()
    {
        await using var host = await BetaTestHost.StartAsync();

        var legacy = await host.Client.GetAsync("/health");
        var live = await host.Client.GetAsync("/health/live");
        var ready = await host.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var readyJson = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", readyJson.RootElement.GetProperty("status").GetString());
        var checks = readyJson.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .ToList();
        Assert.Equal(["database", "ai_provider"], checks);
    }

    [Fact]
    public async Task ReadinessFailsWithoutLeakingExceptionDetails()
    {
        await using var host = await BetaTestHost.StartAsync(configureServices: services =>
            services.AddHealthChecks().AddCheck(
                "broken-dependency",
                () => HealthCheckResult.Unhealthy("secret connection string details"),
                tags: ["ready"]));

        var ready = await host.Client.GetAsync("/health/ready");
        var live = await host.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedEndpointsRejectAnonymousCallers()
    {
        await using var host = await BetaTestHost.StartAsync();

        foreach (var path in new[]
                 {
                     "/api/v1/profile/me", "/api/v1/health-summary", "/api/v1/coach/conversations",
                     "/api/v1/workouts/current", "/api/v1/nutrition/targets", "/api/v1/progress"
                 })
        {
            var response = await host.Client.GetAsync(path);
            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"{path} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task EveryResponseCarriesACorrelationIdAndEchoesAValidOne()
    {
        await using var host = await BetaTestHost.StartAsync();

        var generated = await host.Client.GetAsync("/health");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(ApiRequestMetricsMiddleware.CorrelationHeader, "ios-req-12345678");
        var echoed = await host.Client.SendAsync(request);
        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/health");
        invalid.Headers.Add(ApiRequestMetricsMiddleware.CorrelationHeader, "bad id with spaces");
        var replaced = await host.Client.SendAsync(invalid);

        Assert.False(string.IsNullOrWhiteSpace(
            generated.Headers.GetValues(ApiRequestMetricsMiddleware.CorrelationHeader).Single()));
        Assert.Equal("ios-req-12345678",
            echoed.Headers.GetValues(ApiRequestMetricsMiddleware.CorrelationHeader).Single());
        Assert.NotEqual("bad id with spaces",
            replaced.Headers.GetValues(ApiRequestMetricsMiddleware.CorrelationHeader).Single());
    }

    [Fact]
    public async Task MalformedJsonIsAClientErrorNotAServerError()
    {
        await using var host = await BetaTestHost.StartAsync();

        using var content = new StringContent("{ this is not json", Encoding.UTF8, "application/json");
        var response = await host.Client.PostAsync("/api/v1/auth/apple", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationEndpointsAreRateLimitedPerClient()
    {
        await using var host = await BetaTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthRequestsPerMinute"] = "3"
        });

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "unknown" });
            statuses.Add(last.StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(3));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal("60", last!.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task HealthProbesAreNeverRateLimited()
    {
        await using var host = await BetaTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:RequestsPerMinute"] = "10"
        });

        for (var attempt = 0; attempt < 15; attempt++)
        {
            var response = await host.Client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var limited = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => host.Client.GetAsync("/api/v1/profile/me")));
        Assert.Contains(limited, response => response.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void InvalidRateLimitConfigurationFailsAtStartup()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthRequestsPerMinute"] = "0" })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddOperationalServices(configuration);
        });
    }
}
