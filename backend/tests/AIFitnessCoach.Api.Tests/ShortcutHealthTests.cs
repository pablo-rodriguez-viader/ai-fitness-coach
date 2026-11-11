using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// Free personal install: Apple Health arrives through an iOS Shortcut (the sideloaded app has no
/// HealthKit entitlement). Daily totals, a secret key, the owner's account, health consent.
/// </summary>
public sealed class ShortcutHealthTests
{
    private const string Code = "personal-code-0123456789-abcdefghij";
    private const string Key = "shortcut-key-0123456789-abcdefghijklmn";

    private static Dictionary<string, string?> Settings(string? key = Key) => new()
    {
        ["Auth:PersonalAccessCode"] = Code,
        ["Shortcuts:IngestKey"] = key
    };

    [Fact]
    public async Task TheShortcutFillsTodaysSummaryAndResendingReplacesTheTotals()
    {
        await using var host = await StartAsync(Settings());
        var token = await SignInAndConsentAsync(host);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, Key,
            """{"steps":4200,"activeEnergy":"350,5","restingEnergy":1500,"heartRate":72,"weight":71.5,"sleepHours":7.5}""")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, Key, """{"steps":6100}""")).StatusCode);

        using var summary = await SummaryAsync(host, token);
        var root = summary.RootElement;
        Assert.Equal(6100, root.GetProperty("steps").GetDouble());
        Assert.Equal(350.5, root.GetProperty("activeEnergyKilocalories").GetDouble());
        Assert.Equal(1850.5, root.GetProperty("totalEnergyKilocalories").GetDouble());
        Assert.Equal(72, root.GetProperty("averageHeartRateBeatsPerMinute").GetDouble());
        Assert.Equal(71.5, root.GetProperty("weightKilograms").GetDouble());
        Assert.Equal(7.5 * 3600, root.GetProperty("sleepSeconds").GetDouble());
    }

    [Fact]
    public async Task AWrongKeyIsRejectedAndNothingIsSaved()
    {
        await using var host = await StartAsync(Settings());
        var token = await SignInAndConsentAsync(host);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(host, "wrong-key-0123456789-abcdefghijklmnop", """{"steps":10}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(host, "", """{"steps":10}""")).StatusCode);

        using var summary = await SummaryAsync(host, token);
        var steps = summary.RootElement.GetProperty("steps");
        Assert.True(steps.ValueKind == JsonValueKind.Null || steps.GetDouble() == 0);
    }

    [Fact]
    public async Task TheRouteDoesNotExistWithoutAKey()
    {
        await using var host = await StartAsync(Settings(key: null));

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(host, Key, """{"steps":10}""")).StatusCode);
    }

    [Fact]
    public async Task HealthConsentAndAnOwnerAccountAreRequired()
    {
        await using var host = await StartAsync(Settings());
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(host, Key, """{"steps":10}""")).StatusCode);

        var signIn = await host.Client.PostAsJsonAsync("/api/v1/auth/personal", new { code = Code });
        signIn.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(host, Key, """{"steps":10}""")).StatusCode);
    }

    [Fact]
    public async Task AShortKeyFailsAtStartup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsync(Settings(key: "short"));
        });
    }

    [Fact]
    public void ImplausibleValuesAreDroppedAndTheDayIsTheExternalId()
    {
        var now = new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
        var metrics = ShortcutHealthImport.ToMetrics(
            new ShortcutHealthValues(Steps: 5000, ActiveEnergyKilocalories: -3, RestingEnergyKilocalories: null,
                AverageHeartRate: 0, WeightKilograms: 7150, SleepHours: 7),
            new DateOnly(2026, 11, 11),
            now);

        Assert.Equal(["steps", "sleep_duration"], metrics.Select(metric => metric.Metric));
        Assert.All(metrics, metric => Assert.Equal(now, metric.ObservedAt));
        Assert.Equal("steps:2026-11-11", metrics[0].ExternalId);
    }

    private static async Task<string> SignInAndConsentAsync(BetaTestHost host)
    {
        var response = await host.Client.PostAsJsonAsync("/api/v1/auth/personal", new { code = Code });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString()!;
        using var consent = Authorized(HttpMethod.Put, "/api/v1/consents/health_data", token,
            new { granted = true, version = 1 });
        (await host.Client.SendAsync(consent)).EnsureSuccessStatusCode();
        return token;
    }

    private static async Task<HttpResponseMessage> PostAsync(BetaTestHost host, string key, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/health/shortcut")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (key.Length > 0)
            request.Headers.Add("X-Shortcut-Key", key);
        return await host.Client.SendAsync(request);
    }

    private static async Task<JsonDocument> SummaryAsync(BetaTestHost host, string token)
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        using var request = Authorized(HttpMethod.Get, $"/api/v1/health-summary?from={from}&to={to}", token);
        var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
