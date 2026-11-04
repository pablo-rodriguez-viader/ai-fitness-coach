using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.UseCases.Analytics;
using AIFitnessCoach.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// Private beta scorecard (active users, retention, workouts, Coach, HealthKit, stability,
/// capacity, alerts) and the staged sign-up cap for 10 → 25 → 50 testers.
/// </summary>
public sealed class BetaProgramTests
{
    private static readonly DateOnly To = new(2026, 10, 28);
    private static readonly DateOnly From = To.AddDays(-29);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);
    private static readonly BetaReadinessAnalyzer Analyzer = new();

    // MARK: Active users and retention

    [Fact]
    public void DailyWeeklyAndMonthlyActiveUsersCountDistinctUsers()
    {
        var events = new[]
        {
            Event("a", "session_start", To),
            Event("a", "screen_view", To),
            Event("b", "session_start", To.AddDays(-3)),
            Event("c", "session_start", To.AddDays(-20)),
            Event("d", "session_start", To.AddDays(-40))
        };

        var report = Analyze(events);

        Assert.Equal(1, report.ActiveUsers.Daily);
        Assert.Equal(2, report.ActiveUsers.Weekly);
        Assert.Equal(3, report.ActiveUsers.Monthly);
        Assert.NotNull(report.ActiveUsers.DailyOverMonthly);
    }

    [Fact]
    public void RetentionD1D7D30ComeFromTheClassicRetention()
    {
        var events = new List<AnalyticsEvent>();
        foreach (var user in new[] { "a", "b" })
            events.Add(Event(user, "session_start", To.AddDays(-35)));
        events.Add(Event("a", "session_start", To.AddDays(-34)));
        events.Add(Event("a", "session_start", To.AddDays(-28)));
        events.Add(Event("b", "session_start", To.AddDays(-5)));

        var retention = new RetentionAnalyzer().Analyze(events, To);
        var report = Analyzer.Analyze(events, [], retention, From, To, Now, 2, null);

        Assert.Equal(2, report.Retention.D1.EligibleUsers);
        Assert.Equal(1, report.Retention.D1.RetainedUsers);
        Assert.Equal(0.5, report.Retention.D1.Rate);
        Assert.Equal(1, report.Retention.D7.RetainedUsers);
        Assert.Equal(2, report.Retention.D30.EligibleUsers);
    }

    // MARK: Workouts

    [Fact]
    public void StartedWorkoutsWithoutACompletionWithin24HoursAreAbandoned()
    {
        var day1 = Guid.NewGuid().ToString();
        var day2 = Guid.NewGuid().ToString();
        var day3 = Guid.NewGuid().ToString();
        var events = new[]
        {
            // Completed two hours after opening it.
            Event("a", ProductEventNames.WorkoutStarted, To.AddDays(-3), ("workout_day_id", day1)),
            Event("a", ProductEventNames.WorkoutCompleted, To.AddDays(-3), ("workout_day_id", day1)) with
            {
                OccurredAt = At(To.AddDays(-3)).AddHours(2)
            },
            // Opened twice, never completed: abandoned once.
            Event("b", ProductEventNames.WorkoutStarted, To.AddDays(-5), ("workout_day_id", day2)),
            Event("b", ProductEventNames.WorkoutStarted, To.AddDays(-4), ("workout_day_id", day2)),
            // Opened an hour ago: still in progress, not abandoned yet.
            Event("c", ProductEventNames.WorkoutStarted, To, ("workout_day_id", day3)) with
            {
                OccurredAt = Now.AddHours(-1)
            }
        };

        var report = Analyze(events);

        Assert.Equal(3, report.Workouts.Started);
        Assert.Equal(1, report.Workouts.Completed);
        Assert.Equal(1, report.Workouts.Abandoned);
        Assert.Equal(1, report.Workouts.InProgress);
        Assert.Equal(0.5, report.Workouts.AbandonmentRate);
    }

    // MARK: Coach

    [Fact]
    public void CoachQueriesRepeatsDuplicatesAndFeedbackAreCounted()
    {
        var hash = ProductEventNames.QuestionHash("¿Cómo ha ido mi semana?");
        var request = Guid.NewGuid().ToString();
        var events = new[]
        {
            Event("a", ProductEventNames.CoachQuery, To.AddDays(-3), ("kind", "coach"), ("client_request_id", request), ("question_hash", hash)),
            // A retry of the same request is not a new query.
            Event("a", ProductEventNames.CoachQuery, To.AddDays(-3), ("kind", "coach"), ("client_request_id", request), ("question_hash", hash)),
            Event("a", ProductEventNames.CoachQuery, To.AddDays(-2), ("kind", "coach"), ("client_request_id", Guid.NewGuid().ToString()),
                ("question_hash", ProductEventNames.QuestionHash("cómo ha ido mi semana"))),
            Event("b", ProductEventNames.CoachQuery, To.AddDays(-1), ("kind", "analysis"), ("question_hash", "abc")),
            Event("a", ProductEventNames.CoachFeedback, To.AddDays(-2), ("rating", "positive")),
            Event("a", ProductEventNames.CoachFeedback, To.AddDays(-2), ("rating", "positive")),
            Event("b", ProductEventNames.CoachFeedback, To.AddDays(-1), ("rating", "negative"))
        };

        var report = Analyze(events);

        Assert.Equal(3, report.Coach.Queries);
        Assert.Equal(2, report.Coach.CoachQueries);
        Assert.Equal(1, report.Coach.AnalysisQueries);
        Assert.Equal(2, report.Coach.UsersAsking);
        Assert.Equal(1, report.Coach.RepeatUsers);
        Assert.Equal(1, report.Coach.RepeatedQueries);
        Assert.Equal(1, report.Coach.DuplicateQuestions);
        Assert.Equal(2, report.Coach.PositiveFeedback);
        Assert.Equal(1, report.Coach.NegativeFeedback);
        Assert.Equal(0.667, report.Coach.PositiveRate);
    }

    [Fact]
    public void QuestionHashIgnoresCaseSpacingAndPunctuationButNotContent()
    {
        Assert.Equal(
            ProductEventNames.QuestionHash("¿Cómo ha ido   mi semana?"),
            ProductEventNames.QuestionHash("cómo ha ido mi semana"));
        Assert.NotEqual(
            ProductEventNames.QuestionHash("¿Cómo ha ido mi semana?"),
            ProductEventNames.QuestionHash("¿Cómo ha ido mi mes?"));
        Assert.Equal(16, ProductEventNames.QuestionHash("hola").Length);
    }

    // MARK: HealthKit and stability

    [Fact]
    public void SyncResultsPermissionsAndStabilityAreSummarised()
    {
        var activity = new[]
        {
            Event("a", ProductEventNames.HealthConnected, To.AddDays(-10)),
            Event("b", ProductEventNames.HealthConnected, To.AddDays(-10)),
            Event("a", ProductEventNames.HealthKitDataCheck, To.AddDays(-10), ("readable_types", "0")),
            // A later check with data replaces the earlier empty one.
            Event("a", ProductEventNames.HealthKitDataCheck, To.AddDays(-9), ("readable_types", "5")),
            Event("b", ProductEventNames.HealthKitDataCheck, To.AddDays(-10), ("readable_types", "0")),
            Event("b", ProductEventNames.HealthKitAuthorization, To.AddDays(-10), ("outcome", "failed")),
            Event("a", "session_start", To.AddDays(-1)),
            Event("b", "session_start", To.AddDays(-1))
        };
        var passive = new[]
        {
            Event("a", ProductEventNames.SyncCompleted, To.AddDays(-2)),
            Event("a", ProductEventNames.SyncCompleted, To.AddDays(-1)),
            Event("b", ProductEventNames.SyncCompleted, To.AddDays(-1)),
            Event("b", ProductEventNames.SyncFailed, To.AddDays(-1)),
            Event("b", ProductEventNames.CrashReport, To.AddDays(-1), ("count", "2")) with { AppVersion = "0.9.0" },
            Event("a", ProductEventNames.Error, To.AddDays(-1), ("area", "nutrition_load")) with { AppVersion = "0.9.0" },
            Event("a", ProductEventNames.Error, To.AddDays(-1), ("area", "nutrition_load")) with { AppVersion = "0.9.1" }
        };

        var report = Analyzer.Analyze(activity, passive, Empty, From, To, Now, 2, 10);

        Assert.Equal(3, report.HealthKit.SyncsSucceeded);
        Assert.Equal(1, report.HealthKit.SyncsFailed);
        Assert.Equal(0.75, report.HealthKit.SyncSuccessRate);
        Assert.Equal(1, report.HealthKit.UsersWithSyncFailures);
        Assert.Equal(2, report.HealthKit.UsersConnected);
        Assert.Equal(1, report.HealthKit.UsersPermissionLikelyDenied);
        Assert.Equal(1, report.HealthKit.AuthorizationFailures);
        Assert.Equal(2, report.Stability.Crashes);
        Assert.Equal(1, report.Stability.UsersWithCrashes);
        Assert.Equal(0.5, report.Stability.CrashFreeUsersRate);
        Assert.Equal("nutrition_load", report.Stability.ErrorsByArea.Single().Key);
        Assert.Equal(2, report.Stability.ByBuild.Count);
        Assert.Equal(2, report.Stability.ByBuild.Single(build => build.AppVersion == "0.9.0").Crashes);
        Assert.Contains(report.Alerts, alert => alert.Code == "crash_free_users");
    }

    [Fact]
    public void AlertsNeedASampleAndFlagCapacity()
    {
        var quiet = Analyzer.Analyze(
            [Event("a", "session_start", To)],
            [Event("a", ProductEventNames.SyncFailed, To)],
            Empty, From, To, Now, 9, 10);

        Assert.DoesNotContain(quiet.Alerts, alert => alert.Code == "sync_success");
        Assert.Contains(quiet.Alerts, alert => alert.Code == "beta_capacity");
        Assert.Equal(1, quiet.Capacity.RemainingSeats);
    }

    // MARK: End to end

    [Fact]
    public async Task BackgroundSyncAndCrashEventsAreStoredButDoNotMakeUsersActive()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Analytics:AdminApiKey"] = AdminKey });
        var user = await host.SignInAsync("apple-passive-user");
        await SendEventsAsync(host, user,
            ("sync_completed", new Dictionary<string, string> { ["items"] = "250" }),
            ("sync_failed", new Dictionary<string, string> { ["reason"] = "offline" }),
            ("crash_report", new Dictionary<string, string> { ["count"] = "1", ["signal"] = "SIGSEGV" }),
            ("healthkit_data_check", new Dictionary<string, string> { ["readable_types"] = "0" }));

        var beta = await BetaSectionAsync(host);

        Assert.Equal(1, beta.GetProperty("healthKit").GetProperty("syncsSucceeded").GetInt32());
        Assert.Equal(1, beta.GetProperty("healthKit").GetProperty("syncsFailed").GetInt32());
        Assert.Equal(1, beta.GetProperty("stability").GetProperty("crashes").GetInt32());
        // Only healthkit_data_check (shown while the user is in the app) counts as activity.
        Assert.Equal(1, beta.GetProperty("activeUsers").GetProperty("daily").GetInt32());

        var other = await host.SignInAsync("apple-background-only");
        await SendEventsAsync(host, other, ("sync_completed", new Dictionary<string, string>()));
        var after = await BetaSectionAsync(host);
        Assert.Equal(1, after.GetProperty("activeUsers").GetProperty("daily").GetInt32());
        Assert.Equal(2, after.GetProperty("capacity").GetProperty("registeredUsers").GetInt32());
    }

    [Fact]
    public async Task SignUpsStopAtTheBetaCapButExistingTestersCanSignInAgain()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Beta:MaxUsers"] = "2" });
        await host.SignInAsync("apple-cap-1");
        await host.SignInAsync("apple-cap-2");

        var third = await host.Client.PostAsJsonAsync(
            "/api/v1/auth/apple",
            new { identityToken = "apple-cap-3", nonce = "test-nonce", displayName = "Late tester" });

        Assert.Equal(HttpStatusCode.Forbidden, third.StatusCode);
        using (var json = JsonDocument.Parse(await third.Content.ReadAsStringAsync()))
            Assert.Equal("beta_full", json.RootElement.GetProperty("code").GetString());
        var returning = await host.SignInAsync("apple-cap-1");
        Assert.False(string.IsNullOrEmpty(returning.AccessToken));
    }

    [Theory]
    [InlineData("Beta:MaxUsers", "-1")]
    [InlineData("Beta:KpiIntervalMinutes", "1")]
    public async Task InvalidBetaSettingsFailAtStartup(string key, string value)
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var host = await StartAsync(new Dictionary<string, string?> { [key] = value });
        });
    }

    [Fact]
    public async Task KpiPublisherComputesTheScorecardWithoutAnyoneCallingTheReport()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Beta:MaxUsers"] = "10" });
        var user = await host.SignInAsync("apple-kpi-user");
        await SendEventsAsync(host, user, ("session_start", new Dictionary<string, string>()));
        var publisher = host.App.Services.GetServices<IHostedService>().OfType<BetaKpiPublisher>().Single();

        var beta = await publisher.PublishOnceAsync(CancellationToken.None);

        Assert.NotNull(beta);
        Assert.Equal(1, beta.ActiveUsers.Daily);
        Assert.Equal(9, beta.Capacity.RemainingSeats);
        Assert.Same(beta, publisher.Latest);
    }

    // MARK: Helpers

    private const string AdminKey = "beta-program-admin-key-0123456789abcdef";

    private static readonly RetentionReport Empty = new(To, [], [], []);

    private static BetaReport Analyze(IReadOnlyList<AnalyticsEvent> events) =>
        Analyzer.Analyze(events, [], Empty, From, To, Now, 3, null);

    private static DateTimeOffset At(DateOnly day) => new(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    private static AnalyticsEvent Event(string user, string name, DateOnly day, params (string Key, string Value)[] properties) =>
        new(user, name, At(day), properties.ToDictionary(pair => pair.Key, pair => pair.Value));

    private static async Task SendEventsAsync(
        BetaTestHost host,
        SignedInUser user,
        params (string Name, Dictionary<string, string> Properties)[] events)
    {
        using var request = Authorized(HttpMethod.Post, "/api/v1/telemetry/events", user.AccessToken, new
        {
            appVersion = "0.9.0",
            buildNumber = "21",
            environment = "beta",
            events = events.Select(item => new { name = item.Name, occurredAt = DateTimeOffset.UtcNow, properties = item.Properties })
        });
        (await host.Client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> BetaSectionAsync(BetaTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/analytics/product");
        request.Headers.Add("X-Analytics-Key", AdminKey);
        var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("beta").Clone();
    }
}
