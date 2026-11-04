using System.Net;
using System.Text.Json;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// GDPR: explicit consents recorded as proof, health uploads blocked without consent, erasure of
/// AI and health data without closing the account, and automatic retention.
/// </summary>
public sealed class GdprTests
{
    [Fact]
    public async Task HealthUploadsNeedExplicitConsentAndResumeOnceGranted()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-gdpr-consent", grantHealthConsent: false);

        var blocked = await SyncAsync(host, user, "gdpr-1");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using (var json = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()))
            Assert.Equal("health_consent_required", json.RootElement.GetProperty("code").GetString());

        using var grant = Authorized(HttpMethod.Put, "/api/v1/consents/health_data", user.AccessToken,
            new { granted = true, version = 1 });
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(grant)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SyncAsync(host, user, "gdpr-2")).StatusCode);

        using var list = Authorized(HttpMethod.Get, "/api/v1/consents", user.AccessToken);
        using var listJson = JsonDocument.Parse(await (await host.Client.SendAsync(list)).Content.ReadAsStringAsync());
        var consent = Assert.Single(listJson.RootElement.EnumerateArray());
        Assert.Equal("health_data", consent.GetProperty("kind").GetString());
        Assert.True(consent.GetProperty("granted").GetBoolean());
        Assert.Equal(1, consent.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task ConsentRequestsAreValidated()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-gdpr-validation", grantHealthConsent: false);

        using var unknown = Authorized(HttpMethod.Put, "/api/v1/consents/marketing", user.AccessToken,
            new { granted = true, version = 1 });
        using var missingVersion = Authorized(HttpMethod.Put, "/api/v1/consents/analytics", user.AccessToken,
            new { granted = true });

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(unknown)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(missingVersion)).StatusCode);
    }

    [Fact]
    public async Task DeletingHealthDataRemovesSamplesWithdrawsConsentAndBlocksNewUploads()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-gdpr-health");
        Assert.Equal(HttpStatusCode.OK, (await SyncAsync(host, user, "gdpr-health-1")).StatusCode);

        using var delete = Authorized(HttpMethod.Delete, "/api/v1/account/health-data", user.AccessToken);
        var response = await host.Client.SendAsync(delete);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            Assert.Equal(1, json.RootElement.GetProperty("samplesDeleted").GetInt32());
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            Assert.False(await db.HealthMetrics.AnyAsync(item => item.UserId == user.UserId));
            Assert.True(await db.Users.AnyAsync(item => item.Id == user.UserId), "The account stays.");
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await SyncAsync(host, user, "gdpr-health-2")).StatusCode);
    }

    [Fact]
    public async Task DeletingAIDataRemovesConversationsAndWithdrawsAIConsent()
    {
        // The real AI consent store uses PostgreSQL's ON CONFLICT, which EF InMemory cannot run.
        var aiConsents = new InMemoryAIConsents();
        await using var host = await StartAsync(configureServices: services =>
        {
            services.RemoveAll<AIFitnessCoach.Application.UseCases.AI.ICoachConsentStore>();
            services.AddSingleton<AIFitnessCoach.Application.UseCases.AI.ICoachConsentStore>(aiConsents);
        });
        var user = await host.SignInAsync("apple-gdpr-ai");
        using (var consent = Authorized(HttpMethod.Put, "/api/v1/coach/consent", user.AccessToken, new { granted = true }))
            (await host.Client.SendAsync(consent)).EnsureSuccessStatusCode();
        Assert.True(aiConsents.IsGranted(user.UserId));
        using (var create = Authorized(HttpMethod.Post, "/api/v1/coach/conversations", user.AccessToken, new { }))
            Assert.True((await host.Client.SendAsync(create)).IsSuccessStatusCode);

        using var delete = Authorized(HttpMethod.Delete, "/api/v1/account/ai-data", user.AccessToken);
        var response = await host.Client.SendAsync(delete);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("conversationsDeleted").GetInt32());
            Assert.False(json.RootElement.GetProperty("consentGranted").GetBoolean());
        }
        Assert.False(aiConsents.IsGranted(user.UserId));
        using var conversations = Authorized(HttpMethod.Get, "/api/v1/coach/conversations", user.AccessToken);
        Assert.Equal("[]", (await (await host.Client.SendAsync(conversations)).Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task AIConsentGivenToTheOldTextMustBeGivenAgain()
    {
        // v1 did not name the AI provider; App Store Guideline 5.1.2(i) requires it.
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new FitnessDbContext(options);
        db.Users.Add(User.Create("user-v1", "Ada", DateTimeOffset.UtcNow));
        db.Users.Add(User.Create("user-v2", "Grace", DateTimeOffset.UtcNow));
        db.AIConsents.Add(AIFitnessCoach.AI.Models.AIConsent.Set("user-v1", true, 1, DateTimeOffset.UtcNow));
        db.AIConsents.Add(AIFitnessCoach.AI.Models.AIConsent.Set(
            "user-v2", true, AIFitnessCoach.Application.UseCases.AI.CoachConsentVersion.Current, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var store = new AIFitnessCoach.Infrastructure.AI.CoachConsentStore(db);

        Assert.False((await store.GetAsync("user-v1", CancellationToken.None)).Granted);
        Assert.True((await store.GetAsync("user-v2", CancellationToken.None)).Granted);
    }

    [Fact]
    public async Task ConsentsAreExportedWithTheAccount()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-gdpr-export");

        using var export = Authorized(HttpMethod.Get, "/api/v1/account/export", user.AccessToken);
        var body = await (await host.Client.SendAsync(export)).Content.ReadAsStringAsync();

        Assert.Contains("\"user_consents\"", body);
        Assert.Contains("health_data", body);
    }

    [Fact]
    public async Task RetentionDeletesOnlyRowsOlderThanThePolicy()
    {
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Retention:ProductEventsDays"] = "60",
            ["Retention:SupportReportsDays"] = "30"
        });
        var user = await host.SignInAsync("apple-gdpr-retention");
        var now = DateTimeOffset.UtcNow;
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            db.ProductEvents.Add(ProductEvent.Create(user.UserId, "session_start", now.AddDays(-90), now.AddDays(-90)));
            db.ProductEvents.Add(ProductEvent.Create(user.UserId, "session_start", now.AddDays(-10), now.AddDays(-10)));
            db.SupportReports.Add(SupportReport.Create(user.UserId, "bug", null, "viejo", null, null,
                null, null, null, null, null, now.AddDays(-45)));
            db.SupportReports.Add(SupportReport.Create(user.UserId, "bug", null, "reciente", null, null,
                null, null, null, null, null, now.AddDays(-5)));
            await db.SaveChangesAsync();
        }
        var service = host.App.Services.GetServices<IHostedService>().OfType<DataRetentionService>().Single();

        var result = await service.PurgeOnceAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.ProductEvents);
        Assert.Equal(1, result.SupportReports);
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            Assert.Equal(1, await db.ProductEvents.CountAsync(item => item.UserId == user.UserId));
            Assert.Equal("reciente", (await db.SupportReports.SingleAsync(item => item.UserId == user.UserId)).Message);
            Assert.True(await db.AuthSessions.AnyAsync(item => item.UserId == user.UserId), "Active sessions stay.");
        }
    }

    [Theory]
    [InlineData("Retention:ProductEventsDays", "10")]
    [InlineData("Retention:AIContextSnapshotsDays", "0")]
    public async Task RetentionShorterThanTheMinimumFailsAtStartup(string key, string value)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsync(new Dictionary<string, string?> { [key] = value });
        });
    }

    private sealed class InMemoryAIConsents : AIFitnessCoach.Application.UseCases.AI.ICoachConsentStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AIFitnessCoach.Application.UseCases.AI.CoachConsent> consents = new();

        public bool IsGranted(string userId) => consents.TryGetValue(userId, out var consent) && consent.Granted;

        public Task<AIFitnessCoach.Application.UseCases.AI.CoachConsent> GetAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(consents.GetValueOrDefault(userId) ?? new AIFitnessCoach.Application.UseCases.AI.CoachConsent(false, 0, null));

        public Task SetAsync(string userId, bool granted, int version, DateTimeOffset now, CancellationToken cancellationToken)
        {
            consents[userId] = new AIFitnessCoach.Application.UseCases.AI.CoachConsent(granted, version, granted ? now : null);
            return Task.CompletedTask;
        }
    }

    private static async Task<HttpResponseMessage> SyncAsync(BetaTestHost host, SignedInUser user, string externalId)
    {
        using var sync = Authorized(HttpMethod.Post, "/api/v1/health-metrics/batch", user.AccessToken, new
        {
            userId = user.UserId,
            metrics = new object[]
            {
                new
                {
                    metric = "steps", value = 1200d, unit = "count", observedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    source = "apple_health", externalId, deviceId = "gdpr-device"
                }
            }
        });
        return await host.Client.SendAsync(sync);
    }
}
