using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.Infrastructure.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// Release candidate checks: AI circuit breaker, request timeouts, readiness, and a smoke pass
/// over every route so a new endpoint cannot ship without authentication.
/// </summary>
public sealed class ReleaseCandidateTests
{
    // MARK: Circuit breaker (unit, with the real OpenAIClient over a fake handler)

    [Fact]
    public async Task CircuitOpensAfterConsecutiveOutagesAndRejectsWithoutCallingTheProvider()
    {
        var clock = new ManualClock();
        var handler = new ProviderHandler(HttpStatusCode.ServiceUnavailable);
        var (client, breaker) = CreateClient(handler, clock, threshold: 2);

        await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));
        await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));
        Assert.Equal(CircuitState.Open, breaker.State);
        var callsWhenOpened = handler.Calls;

        var watch = Stopwatch.StartNew();
        var rejected = await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));

        Assert.Equal(503, rejected.StatusCode);
        Assert.Equal(callsWhenOpened, handler.Calls);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "An open circuit must fail fast.");
        // Each request retried once inside OpenAIClient: 2 requests x 2 attempts.
        Assert.Equal(4, callsWhenOpened);
    }

    [Fact]
    public async Task HalfOpenLetsOneTrialThroughAndASuccessClosesTheCircuit()
    {
        var clock = new ManualClock();
        var handler = new ProviderHandler(HttpStatusCode.InternalServerError);
        var (client, breaker) = CreateClient(handler, clock, threshold: 1, breakSeconds: 30);
        await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));
        Assert.Equal(CircuitState.Open, breaker.State);

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(CircuitState.HalfOpen, breaker.State);
        handler.Status = HttpStatusCode.OK;

        var completion = await client.CompleteAsync(Prompt, CancellationToken.None);

        Assert.Equal("hola", completion.Content);
        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    [Fact]
    public async Task FailedTrialReopensTheCircuitForAnotherBreak()
    {
        var clock = new ManualClock();
        var handler = new ProviderHandler(HttpStatusCode.BadGateway);
        var (client, breaker) = CreateClient(handler, clock, threshold: 1, breakSeconds: 30);
        await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(31));

        await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));

        Assert.Equal(CircuitState.Open, breaker.State);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(CircuitState.Open, breaker.State);
    }

    [Fact]
    public async Task ClientErrorsAndDisabledAIDoNotOpenTheCircuit()
    {
        var clock = new ManualClock();
        var badRequest = new ProviderHandler(HttpStatusCode.BadRequest);
        var (client, breaker) = CreateClient(badRequest, clock, threshold: 1);
        for (var attempt = 0; attempt < 3; attempt++)
            await Assert.ThrowsAsync<AIProviderException>(() => client.CompleteAsync(Prompt, CancellationToken.None));
        Assert.Equal(CircuitState.Closed, breaker.State);

        var disabled = CreateClient(new ProviderHandler(HttpStatusCode.OK), clock, threshold: 1, enabled: false);
        for (var attempt = 0; attempt < 3; attempt++)
            await Assert.ThrowsAsync<AIProviderException>(() => disabled.Client.CompleteAsync(Prompt, CancellationToken.None));
        Assert.Equal(CircuitState.Closed, disabled.Breaker.State);
    }

    [Fact]
    public void InvalidCircuitBreakerSettingsFailFast()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:CircuitBreaker:FailureThreshold"] = "0" })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new AICircuitBreaker(configuration, TimeProvider.System, NullLogger<AICircuitBreaker>.Instance));
    }

    // MARK: Readiness and timeouts (end to end)

    [Fact]
    public async Task ReadinessReportsAnOpenAICircuitAsDegradedWithoutFailing()
    {
        await using var host = await StartAsync(
            new Dictionary<string, string?>
            {
                ["AI:Enabled"] = "true",
                ["AI:InputCostPerMillionTokens"] = "0.15",
                ["AI:OutputCostPerMillionTokens"] = "0.6"
            },
            // The key is only read from the AI__ApiKey environment variable in the real host.
            services => services.PostConfigure<AIOptions>(options => options.ApiKey = "test-key"));
        var breaker = host.App.Services.GetRequiredService<AICircuitBreaker>();

        var healthy = await ReadinessAsync(host);
        for (var failure = 0; failure < breaker.FailureThreshold; failure++)
            breaker.RecordFailure();
        var degraded = await ReadinessAsync(host);

        Assert.Equal((HttpStatusCode.OK, "Healthy", "Healthy"), healthy);
        Assert.Equal((HttpStatusCode.OK, "Degraded", "Degraded"), degraded);
    }

    [Fact]
    public async Task SlowAIRequestsAreCutOffWithGatewayTimeout()
    {
        await using var host = await StartAsync(
            new Dictionary<string, string?> { ["RequestTimeouts:AISeconds"] = "1" },
            services =>
            {
                services.RemoveAll<IOpenAIClient>();
                services.AddScoped<IOpenAIClient, HangingClient>();
            });
        var user = await host.SignInAsync("apple-timeout-user");
        using var profile = Authorized(HttpMethod.Put, "/api/v1/profile/me", user.AccessToken,
            new { displayName = "Beta", age = 30, heightCentimeters = 175d, goal = "general_health" });
        (await host.Client.SendAsync(profile)).EnsureSuccessStatusCode();

        var watch = Stopwatch.StartNew();
        using var generate = Authorized(HttpMethod.Post, "/api/v1/workouts/generate", user.AccessToken,
            new { goal = "general_health", days = 1, availableMinutes = 30, equipment = new[] { "Bodyweight" } });
        var response = await host.Client.SendAsync(generate);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
        // The API keeps serving after a timed-out request.
        using var current = Authorized(HttpMethod.Get, "/api/v1/workouts/current", user.AccessToken);
        Assert.NotEqual(HttpStatusCode.InternalServerError, (await host.Client.SendAsync(current)).StatusCode);
    }

    [Theory]
    [InlineData("RequestTimeouts:DefaultSeconds", "0")]
    [InlineData("RequestTimeouts:AISeconds", "9999")]
    public async Task InvalidTimeoutsFailAtStartup(string key, string value)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsync(new Dictionary<string, string?> { [key] = value });
        });
    }

    // MARK: Smoke over every route

    [Fact]
    public async Task EveryNonAnonymousRouteRejectsAnonymousCallersWithoutServerErrors()
    {
        await using var host = await StartAsync();
        var routes = ((IEndpointRouteBuilder)host.App).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (Method: method, Path: SamplePath(endpoint.RoutePattern.RawText!))))
            .Distinct()
            .ToList();

        Assert.True(routes.Count > 40, $"Only {routes.Count} protected routes were found.");
        var failures = new List<string>();
        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await host.Client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AnonymousRoutesAreOnlyTheExpectedOnes()
    {
        await using var host = await StartAsync();
        var anonymous = ((IEndpointRouteBuilder)host.App).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "/api/v1/analytics/product", "/api/v1/app-config", "/api/v1/auth/apple", "/api/v1/auth/personal",
                "/api/v1/auth/refresh", "/api/v1/auth/revoke", "/api/v1/health/shortcut", "/api/v1/support/reports",
                "/health", "/health/live", "/health/ready"
            ],
            anonymous);
    }

    private static string SamplePath(string pattern)
    {
        var path = System.Text.RegularExpressions.Regex.Replace(pattern, "\\{[^}]+\\}", Guid.NewGuid().ToString());
        return "/" + path.TrimStart('/');
    }

    private static async Task<(HttpStatusCode Status, string Overall, string AIProvider)> ReadinessAsync(BetaTestHost host)
    {
        var response = await host.Client.GetAsync("/health/ready");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ai = json.RootElement.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "ai_provider");
        return (response.StatusCode, json.RootElement.GetProperty("status").GetString()!, ai.GetProperty("status").GetString()!);
    }

    // MARK: Helpers

    private static readonly IReadOnlyList<AIChatPromptMessage> Prompt = [new("user", "hola")];

    private static (IOpenAIClient Client, AICircuitBreaker Breaker) CreateClient(
        ProviderHandler handler,
        TimeProvider clock,
        int threshold,
        int breakSeconds = 30,
        bool enabled = true)
    {
        var options = Options.Create(new AIOptions
        {
            Enabled = enabled,
            ApiKey = enabled ? "test-key" : string.Empty,
            InputCostPerMillionTokens = 0.15m,
            OutputCostPerMillionTokens = 0.6m
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:CircuitBreaker:FailureThreshold"] = threshold.ToString(),
            ["AI:CircuitBreaker:BreakSeconds"] = breakSeconds.ToString()
        }).Build();
        var breaker = new AICircuitBreaker(configuration, clock, NullLogger<AICircuitBreaker>.Instance);
        var inner = new OpenAIClient(new HttpClient(handler), options);
        return (new CircuitBreakingOpenAIClient(inner, breaker, options), breaker);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class ProviderHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int calls;
        public HttpStatusCode Status { get; set; } = status;
        public int Calls => Volatile.Read(ref calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            var response = new HttpResponseMessage(Status);
            if (Status == HttpStatusCode.OK)
                response.Content = new StringContent(
                    """{"model":"test","choices":[{"message":{"content":"hola"}}],"usage":{"prompt_tokens":3,"completion_tokens":1}}""",
                    System.Text.Encoding.UTF8,
                    "application/json");
            return Task.FromResult(response);
        }
    }

    /// <summary>Never answers until the request is cancelled.</summary>
    private sealed class HangingClient : IOpenAIClient
    {
        public async Task<AIChatCompletion> CompleteAsync(
            IReadOnlyList<AIChatPromptMessage> messages,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public Task<AIChatCompletion> CompleteJsonAsync(
            IReadOnlyList<AIChatPromptMessage> messages,
            CancellationToken cancellationToken) =>
            CompleteAsync(messages, cancellationToken);
    }
}
