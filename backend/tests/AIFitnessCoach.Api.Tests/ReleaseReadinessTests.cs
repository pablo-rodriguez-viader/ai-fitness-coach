using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Authentication;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// App Store and private-beta requirements: data export, account deletion (with Apple token
/// revocation), Feedback Center / bug reports and remote feature flags.
/// </summary>
public sealed class ReleaseReadinessTests
{
    private const string AdminKey = "release-tests-admin-key-0123456789abcdef";

    [Fact]
    public async Task AppConfigIsAnonymousAndTurnsOffAIFlowsWhileAIIsDisabled()
    {
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["FeatureFlags:nutrition"] = "false",
            ["Release:SupportEmail"] = "beta@aifitnesscoach.example",
            ["Release:PrivacyPolicyUrl"] = "https://aifitnesscoach.example/privacy",
            ["Release:MinimumSupportedBuild"] = "7"
        });

        var response = await host.Client.GetAsync("/api/v1/app-config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("beta", root.GetProperty("environment").GetString());
        Assert.Equal(7, root.GetProperty("minimumSupportedBuild").GetInt32());
        Assert.Equal("beta@aifitnesscoach.example", root.GetProperty("supportEmail").GetString());
        Assert.Equal("https://aifitnesscoach.example/privacy", root.GetProperty("privacyPolicyUrl").GetString());
        var features = root.GetProperty("features");
        Assert.False(features.GetProperty("coach").GetBoolean());
        Assert.False(features.GetProperty("activity_analysis").GetBoolean());
        Assert.False(features.GetProperty("workouts").GetBoolean());
        Assert.False(features.GetProperty("nutrition").GetBoolean());
        Assert.True(features.GetProperty("goals").GetBoolean());
        Assert.True(features.GetProperty("feedback_center").GetBoolean());
    }

    [Theory]
    [InlineData("FeatureFlags:typo_flag", "false")]
    [InlineData("Release:PrivacyPolicyUrl", "http://insecure.example/privacy")]
    [InlineData("Release:SupportEmail", "not-an-email")]
    public async Task InvalidReleaseConfigurationFailsAtStartup(string key, string value)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsync(new Dictionary<string, string?> { [key] = value });
        });
    }

    [Fact]
    public async Task ExportContainsOnlyTheUsersDataAndNoSessionSecrets()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-export-user");
        var other = await host.SignInAsync("apple-export-other");
        await SeedUserDataAsync(host, user, "export-user");
        await SeedUserDataAsync(host, other, "export-other");

        using var request = Authorized(HttpMethod.Get, "/api/v1/account/export", user.AccessToken);
        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString()
            ?? response.Headers.GetValues("Content-Disposition").Single());
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("aifitnesscoach-export-v1", json.RootElement.GetProperty("format").GetString());
        var tables = json.RootElement.GetProperty("tables").EnumerateArray()
            .ToDictionary(table => table.GetProperty("name").GetString()!, table => table.GetProperty("rows"));
        Assert.Equal(1, tables["users"].GetArrayLength());
        Assert.Equal(1, tables["user_profiles"].GetArrayLength());
        Assert.Equal(2, tables["health_metrics"].GetArrayLength());
        Assert.Equal(1, tables["support_reports"].GetArrayLength());
        Assert.True(tables["product_events"].GetArrayLength() >= 1);
        Assert.True(tables.ContainsKey("auth_sessions"));
        Assert.False(tables.ContainsKey("refresh_tokens"));
        Assert.DoesNotContain("token_hash", body);
        Assert.DoesNotContain(other.UserId, body);
        Assert.DoesNotContain("export-other", body);
    }

    [Fact]
    public async Task DeletingTheAccountRemovesEveryRowAndEndsTheSession()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-delete-user");
        var other = await host.SignInAsync("apple-delete-other");
        await SeedUserDataAsync(host, user, "delete-user");
        await SeedUserDataAsync(host, other, "delete-other");

        using var delete = Authorized(HttpMethod.Post, "/api/v1/account/delete", user.AccessToken,
            new { authorizationCode = (string?)null });
        var response = await host.Client.SendAsync(delete);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.True(json.RootElement.GetProperty("deleted").GetBoolean());
            // Apple:TeamId/KeyId/PrivateKey are not set in tests.
            Assert.Equal("NotConfigured", json.RootElement.GetProperty("appleRevocation").GetString());
        }

        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            Assert.False(await db.Users.AnyAsync(item => item.Id == user.UserId));
            Assert.False(await db.HealthMetrics.AnyAsync(item => item.UserId == user.UserId));
            Assert.False(await db.UserProfiles.AnyAsync(item => item.UserId == user.UserId));
            Assert.False(await db.ProductEvents.AnyAsync(item => item.UserId == user.UserId));
            Assert.False(await db.SupportReports.AnyAsync(item => item.UserId == user.UserId));
            Assert.False(await db.AuthSessions.AnyAsync(item => item.UserId == user.UserId));
            Assert.False(await db.UserIdentities.AnyAsync(item => item.UserId == user.UserId));
            Assert.True(await db.HealthMetrics.AnyAsync(item => item.UserId == other.UserId));
            Assert.True(await db.SupportReports.AnyAsync(item => item.UserId == other.UserId));
        }

        using (var scope = host.App.Services.CreateScope())
        {
            // Refresh rotation uses SELECT ... FOR UPDATE, which EF InMemory cannot run, so the
            // refresh endpoint is not called here: the token rows themselves must be gone.
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            var remainingSessions = await db.AuthSessions.Select(session => session.Id).ToListAsync();
            Assert.All(await db.RefreshTokens.ToListAsync(),
                token => Assert.Contains(token.AuthSessionId, remainingSessions));
            Assert.Equal(1, await db.RefreshTokens.CountAsync());
        }

        using var afterDelete = Authorized(HttpMethod.Get, "/api/v1/profile/me", user.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(afterDelete)).StatusCode);

        // Signing in again with the same Apple ID starts a brand new, empty account.
        var again = await host.SignInAsync("apple-delete-user");
        Assert.NotEqual(user.UserId, again.UserId);
        using var profile = Authorized(HttpMethod.Get, "/api/v1/profile/me", again.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(profile)).StatusCode);
    }

    [Fact]
    public async Task DeleteWithoutBodyIsAccepted()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-delete-nobody");

        using var delete = Authorized(HttpMethod.Delete, "/api/v1/account", user.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(delete)).StatusCode);
    }

    [Fact]
    public void EveryTableIsCoveredByExportAndDeletion()
    {
        // A table with user data that is not reachable from users would survive account deletion.
        using var db = new FitnessDbContext(new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var covered = AccountDataStore.UserOwnedEntityTypes(db.Model).Select(type => type.ClrType).ToHashSet();
        var missing = db.Model.GetEntityTypes()
            .Where(type => !type.IsOwned() && !covered.Contains(type.ClrType))
            .Select(type => type.ClrType.Name)
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task FeedbackAndBugReportsAreStoredAndListedForTheTeam()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Analytics:AdminApiKey"] = AdminKey });
        var user = await host.SignInAsync("apple-support-user");

        using var bug = Authorized(HttpMethod.Post, "/api/v1/support/reports", user.AccessToken, new
        {
            kind = "bug",
            category = "sync",
            message = "Los pasos de hoy no aparecen en el Dashboard.",
            appVersion = "0.9.0",
            buildNumber = "14",
            osVersion = "18.1",
            deviceModel = "iPhone16,1",
            diagnostics = new Dictionary<string, string> { ["pending_sync_items"] = "12", ["network"] = "wifi" }
        });
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(bug)).StatusCode);

        using var feedback = Authorized(HttpMethod.Post, "/api/v1/support/reports", user.AccessToken, new
        {
            kind = "feedback",
            message = "Me gusta el resumen semanal.",
            rating = 5
        });
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(feedback)).StatusCode);

        using var empty = Authorized(HttpMethod.Post, "/api/v1/support/reports", user.AccessToken,
            new { kind = "bug", message = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(empty)).StatusCode);
        using var badKind = Authorized(HttpMethod.Post, "/api/v1/support/reports", user.AccessToken,
            new { kind = "spam", message = "hola" });
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(badKind)).StatusCode);
        using var badRating = Authorized(HttpMethod.Post, "/api/v1/support/reports", user.AccessToken,
            new { kind = "feedback", message = "hola", rating = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(badRating)).StatusCode);

        var anonymous = await host.Client.PostAsJsonAsync("/api/v1/support/reports", new { kind = "bug", message = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/v1/support/reports")).StatusCode);
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/support/reports?kind=bug");
        list.Headers.Add("X-Analytics-Key", AdminKey);
        var listResponse = await host.Client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var json = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var report = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("sync", report.GetProperty("category").GetString());
        Assert.Equal("14", report.GetProperty("buildNumber").GetString());
        Assert.Equal("12", report.GetProperty("diagnostics").GetProperty("pending_sync_items").GetString());
    }

    [Fact]
    public async Task SupportInboxDoesNotExistWithoutAnAdminKey()
    {
        await using var host = await StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/v1/support/reports")).StatusCode);
    }

    [Fact]
    public async Task AppleRevokerExchangesTheCodeAndRevokesTheRefreshToken()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new RecordingAppleHandler();
        var revoker = new AppleTokenRevoker(
            new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Apple:Audience"] = "com.example.aifitnesscoach",
                ["Apple:TeamId"] = "TEAMID1234",
                ["Apple:KeyId"] = "KEYID12345",
                ["Apple:PrivateKey"] = key.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n")
            }).Build(),
            TimeProvider.System,
            NullLogger<AppleTokenRevoker>.Instance);

        var outcome = await revoker.RevokeAsync("fresh-code", CancellationToken.None);

        Assert.Equal(AppleRevocationOutcome.Revoked, outcome);
        Assert.Equal(["/auth/token", "/auth/revoke"], handler.Paths);
        Assert.Contains("code=fresh-code", handler.Bodies[0]);
        Assert.Contains("grant_type=authorization_code", handler.Bodies[0]);
        Assert.Contains("token=apple-refresh", handler.Bodies[1]);
        Assert.Contains("token_type_hint=refresh_token", handler.Bodies[1]);
        Assert.Contains("client_secret=ey", handler.Bodies[1]);
    }

    [Fact]
    public async Task AppleRevokerReportsMissingConfigurationAndCode()
    {
        var unconfigured = new AppleTokenRevoker(
            new HttpClient(new RecordingAppleHandler()),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            NullLogger<AppleTokenRevoker>.Instance);
        Assert.Equal(AppleRevocationOutcome.NotConfigured, await unconfigured.RevokeAsync("code", CancellationToken.None));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configured = new AppleTokenRevoker(
            new HttpClient(new RecordingAppleHandler()),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Apple:Audience"] = "com.example.aifitnesscoach",
                ["Apple:TeamId"] = "TEAMID1234",
                ["Apple:KeyId"] = "KEYID12345",
                ["Apple:PrivateKey"] = key.ExportPkcs8PrivateKeyPem()
            }).Build(),
            TimeProvider.System,
            NullLogger<AppleTokenRevoker>.Instance);
        Assert.Equal(AppleRevocationOutcome.MissingAuthorizationCode, await configured.RevokeAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ApiStartsWithTheOpenTelemetryExporterEnabled()
    {
        // Nothing listens on this port: exports fail in the background and requests are unaffected.
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpEndpoint"] = "http://127.0.0.1:4317"
        });

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/v1/app-config")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public void NewClientEventsAreKnownMetricTags()
    {
        foreach (var name in new[]
        {
            "healthkit_authorization", "healthkit_observer_update", "healthkit_query_failed",
            "ai_request", "support_report_sent", "account_deleted", "app_config_loaded"
        })
        {
            Assert.Contains(name, AIFitnessCoach.Api.Endpoints.TelemetryEndpoints.KnownEvents);
        }
    }

    private static async Task SeedUserDataAsync(BetaTestHost host, SignedInUser user, string marker)
    {
        using var profile = Authorized(HttpMethod.Put, "/api/v1/profile/me", user.AccessToken,
            new { displayName = marker, age = 30, heightCentimeters = 175d, goal = "general_health" });
        (await host.Client.SendAsync(profile)).EnsureSuccessStatusCode();

        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var sync = Authorized(HttpMethod.Post, "/api/v1/health-metrics/batch", user.AccessToken, new
        {
            userId = user.UserId,
            metrics = new object[]
            {
                new { metric = "steps", value = 3000d, unit = "count", observedAt, source = "apple_health", externalId = $"{marker}-steps", deviceId = "device" },
                new { metric = "body_mass", value = 70d, unit = "kg", observedAt, source = "apple_health", externalId = $"{marker}-weight", deviceId = "device" }
            }
        });
        (await host.Client.SendAsync(sync)).EnsureSuccessStatusCode();

        using var telemetry = Authorized(HttpMethod.Post, "/api/v1/telemetry/events", user.AccessToken, new
        {
            appVersion = "0.9.0",
            buildNumber = "14",
            events = new object[] { new { name = "session_start", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string>() } }
        });
        (await host.Client.SendAsync(telemetry)).EnsureSuccessStatusCode();

        using var report = Authorized(HttpMethod.Post, "/api/v1/support/reports", user.AccessToken,
            new { kind = "support", message = $"Ayuda {marker}" });
        (await host.Client.SendAsync(report)).EnsureSuccessStatusCode();
    }

    private sealed class RecordingAppleHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return request.RequestUri.AbsolutePath == "/auth/token"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { access_token = "apple-access", refresh_token = "apple-refresh" })
                }
                : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
