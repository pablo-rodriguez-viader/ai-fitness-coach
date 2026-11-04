using System.Net;
using System.Text.Json;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// End-to-end journeys a beta tester goes through on day one, run against the real pipeline
/// (JWT validation, session checks, rate limiting, error handling) instead of test auth.
/// </summary>
public sealed class BetaUserJourneyTests
{
    [Fact]
    public async Task NewUserSignsInCompletesProfileSyncsHealthAndSeesTodaysSummary()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync();

        using var missingProfile = Authorized(HttpMethod.Get, "/api/v1/profile/me", user.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(missingProfile)).StatusCode);

        using var saveProfile = Authorized(HttpMethod.Put, "/api/v1/profile/me", user.AccessToken,
            new { displayName = "Beta", age = 34, heightCentimeters = 172d, goal = "general_health" });
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(saveProfile)).StatusCode);

        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        using var sync = Authorized(HttpMethod.Post, "/api/v1/health-metrics/batch", user.AccessToken, new
        {
            userId = user.UserId,
            metrics = new object[]
            {
                Sample("steps", 4200, "count", observedAt, "journey-steps"),
                Sample("active_energy", 310, "kcal", observedAt, "journey-active"),
                Sample("basal_energy", 1500, "kcal", observedAt, "journey-basal"),
                Sample("body_mass", 71.5, "kg", observedAt, "journey-weight"),
                // A clock-skewed sample must not block the rest of the queue.
                Sample("steps", 99, "count", DateTimeOffset.UtcNow.AddHours(3), "journey-future")
            }
        });
        var syncResponse = await host.Client.SendAsync(sync);
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);
        using (var syncJson = JsonDocument.Parse(await syncResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(4, syncJson.RootElement.GetProperty("insertedCount").GetInt32());
            Assert.Equal(1, syncJson.RootElement.GetProperty("rejectedCount").GetInt32());
        }

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        using var summary = Authorized(HttpMethod.Get, $"/api/v1/health-summary?from={from}&to={to}", user.AccessToken);
        var summaryResponse = await host.Client.SendAsync(summary);
        summaryResponse.EnsureSuccessStatusCode();
        using var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        Assert.Equal(4200, summaryJson.RootElement.GetProperty("steps").GetDouble());
        Assert.Equal(1810, summaryJson.RootElement.GetProperty("totalEnergyKilocalories").GetDouble());
        Assert.Equal(71.5, summaryJson.RootElement.GetProperty("weightKilograms").GetDouble());
    }

    [Fact]
    public async Task WorkoutGenerationReportsUnavailableWhenAIIsDisabled()
    {
        // Workout plans are produced by the AI provider: with AI:Enabled=false the beta cannot
        // offer this flow, and the app must get a clear 503 rather than a generic failure.
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-no-ai-user");
        await SaveProfileAsync(host, user);

        using var generate = Authorized(HttpMethod.Post, "/api/v1/workouts/generate", user.AccessToken,
            new { goal = "general_health", days = 1, availableMinutes = 30, equipment = new[] { "Bodyweight" } });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.SendAsync(generate)).StatusCode);
    }

    [Fact]
    public async Task UserGeneratesAWorkoutPlanAndRecordsFeedbackIntoHistory()
    {
        await using var host = await StartAsync(configureServices: services =>
        {
            services.RemoveAll<IOpenAIClient>();
            services.AddScoped<IOpenAIClient, OneDayPlanClient>();
        });
        var user = await host.SignInAsync("apple-workout-user");
        await SaveProfileAsync(host, user);

        using var noPlan = Authorized(HttpMethod.Get, "/api/v1/workouts/current", user.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(noPlan)).StatusCode);

        using var generate = Authorized(HttpMethod.Post, "/api/v1/workouts/generate", user.AccessToken,
            new { goal = "general_health", days = 1, availableMinutes = 30, equipment = new[] { "Bodyweight" } });
        var generated = await host.Client.SendAsync(generate);
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        using var planJson = JsonDocument.Parse(await generated.Content.ReadAsStringAsync());
        var firstDay = planJson.RootElement.GetProperty("days")[0].GetProperty("id").GetGuid();

        using var feedback = Authorized(HttpMethod.Post, "/api/v1/workouts/feedback", user.AccessToken,
            new { workoutDayId = firstDay, completed = true, difficulty = "Appropriate", comment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(feedback)).StatusCode);

        using var history = Authorized(HttpMethod.Get, "/api/v1/workouts/history", user.AccessToken);
        var historyResponse = await host.Client.SendAsync(history);
        historyResponse.EnsureSuccessStatusCode();
        using var historyJson = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.NotEmpty(historyJson.RootElement.EnumerateArray());

        using var current = Authorized(HttpMethod.Get, "/api/v1/workouts/current", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(current)).StatusCode);
    }

    [Fact]
    public async Task NutritionAndProgressHaveUsableEmptyStatesForANewUser()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-nutrition-user");
        await SaveProfileAsync(host, user);

        // Not configured yet: a validation problem the app shows as "set up your profile".
        using var targets = Authorized(HttpMethod.Get, "/api/v1/nutrition/targets", user.AccessToken);
        var targetsResponse = await host.Client.SendAsync(targets);
        Assert.Equal(HttpStatusCode.BadRequest, targetsResponse.StatusCode);
        Assert.Contains("nutrition profile", await targetsResponse.Content.ReadAsStringAsync());

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        using var summary = Authorized(HttpMethod.Get, $"/api/v1/nutrition/summary?date={today}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(summary)).StatusCode);

        using var progress = Authorized(HttpMethod.Get, "/api/v1/progress", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(progress)).StatusCode);

        using var goals = Authorized(HttpMethod.Get, "/api/v1/goals", user.AccessToken);
        var goalsResponse = await host.Client.SendAsync(goals);
        goalsResponse.EnsureSuccessStatusCode();
        using var goalsJson = JsonDocument.Parse(await goalsResponse.Content.ReadAsStringAsync());
        Assert.Empty(goalsJson.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task LogoutRevokesTheSessionForEveryLaterRequest()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-logout-user");

        using var beforeLogout = Authorized(HttpMethod.Get, "/api/v1/goals", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(beforeLogout)).StatusCode);

        using var logout = Authorized(HttpMethod.Post, "/api/v1/auth/logout", user.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(logout)).StatusCode);

        using var afterLogout = Authorized(HttpMethod.Get, "/api/v1/goals", user.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(afterLogout)).StatusCode);
    }

    [Fact]
    public async Task UsersCannotSeeOrWriteEachOthersHealthData()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync("apple-owner");
        var other = await host.SignInAsync("apple-other");
        Assert.NotEqual(owner.UserId, other.UserId);

        using var spoofed = Authorized(HttpMethod.Post, "/api/v1/health-metrics/batch", other.AccessToken, new
        {
            userId = owner.UserId,
            metrics = new[] { Sample("steps", 10, "count", DateTimeOffset.UtcNow.AddMinutes(-1), "spoof") }
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(spoofed)).StatusCode);

        using var ownerSync = Authorized(HttpMethod.Post, "/api/v1/health-metrics/batch", owner.AccessToken, new
        {
            userId = owner.UserId,
            metrics = new[] { Sample("steps", 500, "count", DateTimeOffset.UtcNow.AddMinutes(-1), "owner-steps") }
        });
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(ownerSync)).StatusCode);

        using var otherLatest = Authorized(HttpMethod.Get, "/api/v1/health-metrics/latest", other.AccessToken);
        var latest = await host.Client.SendAsync(otherLatest);
        latest.EnsureSuccessStatusCode();
        using var latestJson = JsonDocument.Parse(await latest.Content.ReadAsStringAsync());
        Assert.Empty(latestJson.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task AppTelemetryIsAcceptedAndInvalidEventsAreDropped()
    {
        await using var host = await StartAsync();
        var user = await host.SignInAsync("apple-telemetry-user");

        using var telemetry = Authorized(HttpMethod.Post, "/api/v1/telemetry/events", user.AccessToken, new
        {
            appVersion = "0.9.0",
            buildNumber = "12",
            osVersion = "18.1",
            deviceModel = "iPhone16,1",
            events = new object[]
            {
                new { name = "app_launch", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string>() },
                new { name = "crash_report", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string> { ["count"] = "1", ["signal"] = "SIGSEGV" } },
                new { name = "sync_completed", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string> { ["items"] = "250" } },
                new { name = "Not A Valid Name", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string>() }
            }
        });
        var response = await host.Client.SendAsync(telemetry);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("dropped").GetInt32());

        var anonymous = await host.Client.PostAsync(
            "/api/v1/telemetry/events",
            new StringContent("""{"events":[{"name":"app_launch"}]}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private sealed class OneDayPlanClient : IOpenAIClient
    {
        public Task<AIChatCompletion> CompleteAsync(
            IReadOnlyList<AIChatPromptMessage> messages,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AIChatCompletion(
                """{"goal":"general_health","days":[{"title":"Sesión controlada","recoveryNote":"Descansa entre ejercicios.","exercises":[{"name":"Sentadillas","sets":"3","repetitions":"8","durationMinutes":20,"restSeconds":60,"instructions":"Mantén un ritmo cómodo."}]}]}""",
                "test-model",
                100,
                80));
    }

    private static object Sample(string metric, double value, string unit, DateTimeOffset observedAt, string externalId) =>
        new { metric, value, unit, observedAt, source = "apple_health", externalId, deviceId = "journey-device" };

    private static async Task SaveProfileAsync(BetaTestHost host, SignedInUser user)
    {
        using var saveProfile = Authorized(HttpMethod.Put, "/api/v1/profile/me", user.AccessToken,
            new { displayName = "Beta", age = 30, heightCentimeters = 175d, goal = "general_health" });
        (await host.Client.SendAsync(saveProfile)).EnsureSuccessStatusCode();
    }
}
