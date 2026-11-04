using System.Net;
using System.Text;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.Api.Endpoints;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.AI;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

/// <summary>Locks in the fixes made while preparing the private beta.</summary>
public sealed class BetaRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    // Regression: one bad sample used to fail the whole batch, and the app kept resending it
    // first, so no newer data ever reached the server.
    [Fact]
    public async Task DeviceSyncSkipsInvalidSamplesAndStoresTheRest()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        await dbContext.SaveChangesAsync();
        var useCase = new SyncHealthMetricsUseCase(new HealthMetricRepository(dbContext), new FixedTime(Now));

        var result = await useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand(
            [
                new CreateHealthMetricCommand("steps", 100, "count", Now.AddMinutes(-5), "apple_health", "ok-1", "device"),
                new CreateHealthMetricCommand("steps", 100, "count", Now.AddHours(2), "apple_health", "future", "device"),
                new CreateHealthMetricCommand("steps", 100, "kg", Now.AddMinutes(-5), "apple_health", "bad-unit", "device"),
                new CreateHealthMetricCommand("steps", 100, "count", Now.AddMinutes(-5), "apple_health", "no-device", ""),
                new CreateHealthMetricCommand("heart_rate", 62, "count/min", Now.AddMinutes(-4), "apple_health", "ok-2", "device")
            ]),
            CancellationToken.None,
            skipInvalidItems: true);

        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(3, result.RejectedCount);
        Assert.Equal(2, await dbContext.HealthMetrics.CountAsync());
    }

    [Fact]
    public async Task DeviceSyncWithOnlyInvalidSamplesSucceedsWithoutWriting()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        await dbContext.SaveChangesAsync();
        var useCase = new SyncHealthMetricsUseCase(new HealthMetricRepository(dbContext), new FixedTime(Now));

        var result = await useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand(
                [new CreateHealthMetricCommand("steps", 1, "count", Now.AddDays(1), "apple_health", "future", "device")]),
            CancellationToken.None,
            skipInvalidItems: true);

        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(0, await dbContext.HealthMetrics.CountAsync());
    }

    // Regression: a lost refresh response made the next refresh look like token theft and
    // revoked the whole session, logging the tester out.
    [Fact]
    public void LostRotationIsRecoverableOnlyWhileTheReplacementIsUnusedAndRecent()
    {
        var sessionId = Guid.NewGuid();
        var rotatedAt = Now;
        var original = RefreshToken.Create(Guid.NewGuid(), sessionId, Hash('a'), Now.AddDays(-1), Now.AddDays(29));
        original.Revoke(rotatedAt, Hash('b'));
        var unusedReplacement = RefreshToken.Create(Guid.NewGuid(), sessionId, Hash('b'), rotatedAt, rotatedAt.AddDays(30));

        Assert.True(original.CanRecoverLostRotation(unusedReplacement, rotatedAt.AddMinutes(10)));
        Assert.False(original.CanRecoverLostRotation(
            unusedReplacement,
            rotatedAt + RefreshToken.LostRotationRecoveryWindow + TimeSpan.FromSeconds(1)));
        Assert.False(original.CanRecoverLostRotation(null, rotatedAt.AddMinutes(1)));

        var otherSession = RefreshToken.Create(Guid.NewGuid(), Guid.NewGuid(), Hash('b'), rotatedAt, rotatedAt.AddDays(30));
        Assert.False(original.CanRecoverLostRotation(otherSession, rotatedAt.AddMinutes(1)));

        var usedReplacement = RefreshToken.Create(Guid.NewGuid(), sessionId, Hash('b'), rotatedAt, rotatedAt.AddDays(30));
        usedReplacement.Revoke(rotatedAt.AddMinutes(2), Hash('c'));
        Assert.False(original.CanRecoverLostRotation(usedReplacement, rotatedAt.AddMinutes(3)));

        var neverRotated = RefreshToken.Create(Guid.NewGuid(), sessionId, Hash('d'), Now, Now.AddDays(30));
        Assert.False(neverRotated.CanRecoverLostRotation(unusedReplacement, Now));
        Assert.Throws<InvalidOperationException>(() => neverRotated.Supersede(Hash('e')));
    }

    [Fact]
    public void SupersedingKeepsTheOriginalRevocationTime()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), Guid.NewGuid(), Hash('a'), Now, Now.AddDays(30));
        token.Revoke(Now.AddMinutes(1), Hash('b'));

        token.Supersede(Hash('c'));

        Assert.Equal(Now.AddMinutes(1), token.RevokedAt);
        Assert.Equal(Hash('c'), token.ReplacedByTokenHash);
    }

    [Fact]
    public async Task AIProviderTransientFailureIsRetriedOnce()
    {
        var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var client = CreateOpenAIClient(handler);

        var result = await client.CompleteAsync([new AIChatPromptMessage("user", "hola")], CancellationToken.None);

        Assert.Equal(2, handler.Calls);
        Assert.Equal("ok", result.Content);
    }

    [Fact]
    public async Task AIProviderClientErrorsAreNotRetried()
    {
        var handler = new SequenceHandler(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        var client = CreateOpenAIClient(handler);

        var exception = await Assert.ThrowsAsync<AIProviderException>(() =>
            client.CompleteAsync([new AIChatPromptMessage("user", "hola")], CancellationToken.None));

        Assert.Equal(1, handler.Calls);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task AIProviderPersistentOutageFailsAfterTheRetryBudget()
    {
        var handler = new SequenceHandler(
            HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.OK);
        var client = CreateOpenAIClient(handler);

        var exception = await Assert.ThrowsAsync<AIProviderException>(() =>
            client.CompleteAsync([new AIChatPromptMessage("user", "hola")], CancellationToken.None));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(502, exception.StatusCode);
    }

    [Theory]
    [InlineData("app_launch", true)]
    [InlineData("sync_completed", true)]
    [InlineData("App Launch", false)]
    [InlineData("", false)]
    [InlineData("x", false)]
    public void TelemetryEventNamesAreValidated(string name, bool expected)
    {
        var accepted = TelemetryEndpoints.TryNormalize(
            new ClientTelemetryEvent(name, Now, null),
            out _,
            out _);

        Assert.Equal(expected, accepted);
    }

    [Fact]
    public void TelemetryPropertiesAreBoundedAndTruncated()
    {
        var longValue = new string('v', 500);
        Assert.True(TelemetryEndpoints.TryNormalize(
            new ClientTelemetryEvent("error", Now, new Dictionary<string, string> { ["detail"] = longValue }),
            out _,
            out var properties));
        Assert.Equal(TelemetryEndpoints.MaximumValueLength, properties["detail"].Length);

        var tooMany = Enumerable.Range(0, TelemetryEndpoints.MaximumProperties + 1)
            .ToDictionary(index => $"key_{index}", index => "value");
        Assert.False(TelemetryEndpoints.TryNormalize(new ClientTelemetryEvent("error", Now, tooMany), out _, out _));
        Assert.False(TelemetryEndpoints.TryNormalize(
            new ClientTelemetryEvent("error", Now, new Dictionary<string, string> { ["Bad Key"] = "x" }),
            out _,
            out _));
    }

    private static FitnessDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static string Hash(char value) => new(value, 64);

    private static OpenAIClient CreateOpenAIClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new AIOptions
            {
                Enabled = true,
                ApiKey = "test-secret",
                Model = "gpt-4o-mini",
                Endpoint = "https://api.openai.com/v1/"
            }));

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
            {
                response.Content = new StringContent(
                    """{"model":"test-model","choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":3,"completion_tokens":1}}""",
                    Encoding.UTF8,
                    "application/json");
            }
            else
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            }
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
