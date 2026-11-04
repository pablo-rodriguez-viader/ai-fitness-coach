using System.Net;
using System.Text.Json;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.Api.Endpoints;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>End to end: app actions become product events, the user sees their engagement
/// summary and the team gets the product report.</summary>
public sealed class EngagementJourneyTests
{
    private const string AnalyticsKey = "beta-analytics-key-0123456789abcdef";

    [Fact]
    public async Task ActionsFeedTheEngagementSummaryAndTheProductReport()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Analytics:AdminApiKey"] = AnalyticsKey });
        var user = await host.SignInAsync("apple-engaged-user");
        using (var profile = Authorized(HttpMethod.Put, "/api/v1/profile/me", user.AccessToken,
                   new { displayName = "Beta", age = 30, heightCentimeters = 175d, goal = "general_health" }))
            (await host.Client.SendAsync(profile)).EnsureSuccessStatusCode();

        using (var telemetry = Authorized(HttpMethod.Post, "/api/v1/telemetry/events", user.AccessToken, new
               {
                   appVersion = "0.9.0",
                   events = new object[]
                   {
                       new { name = "session_start", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string>() },
                       new { name = "screen_view", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string> { ["screen"] = "nutrition" } },
                       new { name = "app_background", occurredAt = DateTimeOffset.UtcNow, properties = new Dictionary<string, string>() }
                   }
               }))
            Assert.Equal(HttpStatusCode.Accepted, (await host.Client.SendAsync(telemetry)).StatusCode);

        using (var goal = Authorized(HttpMethod.Post, "/api/v1/goals", user.AccessToken, new { type = "general_health" }))
            (await host.Client.SendAsync(goal)).EnsureSuccessStatusCode();
        using (var meal = Authorized(HttpMethod.Post, "/api/v1/nutrition/log", user.AccessToken, new
               {
                   mealType = "Lunch",
                   name = "Ensalada",
                   calories = 450d,
                   proteinGrams = 30d,
                   fatGrams = 15d,
                   carbohydrateGrams = 40d
               }))
            (await host.Client.SendAsync(meal)).EnsureSuccessStatusCode();

        using var summaryRequest = Authorized(HttpMethod.Get, "/api/v1/engagement/summary?utcOffsetMinutes=0", user.AccessToken);
        var summaryResponse = await host.Client.SendAsync(summaryRequest);
        summaryResponse.EnsureSuccessStatusCode();
        using var summary = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        var streak = summary.RootElement.GetProperty("streak");
        Assert.Equal(1, streak.GetProperty("current").GetInt32());
        Assert.True(streak.GetProperty("activeToday").GetBoolean());
        var achievements = summary.RootElement.GetProperty("achievements").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("unlocked").GetBoolean());
        Assert.True(achievements["first_goal"]);
        Assert.False(achievements["first_workout"]);
        var reminderKinds = summary.RootElement.GetProperty("reminders").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .ToArray();
        Assert.Contains("weekly_summary", reminderKinds);
        Assert.DoesNotContain("daily_goal", reminderKinds); // a meal was logged today
        Assert.Equal(1, summary.RootElement.GetProperty("weeklySummary").GetProperty("mealsLogged").GetInt32());

        var anonymous = await host.Client.GetAsync("/api/v1/analytics/product");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var wrongKey = new HttpRequestMessage(HttpMethod.Get, "/api/v1/analytics/product");
        wrongKey.Headers.Add(EngagementEndpoints.AnalyticsKeyHeader, "not-the-key-not-the-key-not-the-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(wrongKey)).StatusCode);

        using var reportRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/analytics/product");
        reportRequest.Headers.Add(EngagementEndpoints.AnalyticsKeyHeader, AnalyticsKey);
        var reportResponse = await host.Client.SendAsync(reportRequest);
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        var metrics = report.RootElement.GetProperty("metrics");
        Assert.Equal(1, metrics.GetProperty("totalUsers").GetInt32());
        Assert.Equal(1, metrics.GetProperty("sessions").GetInt32());
        Assert.Equal(1, metrics.GetProperty("goalsCreated").GetInt32());
        Assert.Equal(1, metrics.GetProperty("mealsLogged").GetInt32());
        Assert.Equal(1, metrics.GetProperty("weeklyActiveUsers").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("dropout").GetProperty("usersAnalyzed").GetInt32());
        var features = report.RootElement.GetProperty("insights").GetProperty("mostUsedFeatures").EnumerateArray()
            .Select(item => item.GetProperty("feature").GetString())
            .ToArray();
        Assert.Contains("nutrition", features);
        Assert.Contains("goals", features);
    }

    [Fact]
    public async Task ProductReportDoesNotExistWithoutAConfiguredKey()
    {
        await using var host = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/analytics/product");
        request.Headers.Add(EngagementEndpoints.AnalyticsKeyHeader, "anything-anything-anything-anything");

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task EngagementSummaryRequiresSignIn()
    {
        await using var host = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/v1/engagement/summary")).StatusCode);
    }

    // Regression: no code path ever completed a goal, so "goals completed" was always 0.
    [Fact]
    public async Task ReachedWeightGoalIsCompletedAndRecorded()
    {
        var now = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        await using var dbContext = new FitnessDbContext(new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        dbContext.Users.Add(User.Create("user-1", "Ada", now.AddDays(-60)));
        var reached = Goal.Create("user-1", GoalType.FatLoss, 70, DateOnly.FromDateTime(now.AddDays(-30).UtcDateTime), null, now.AddDays(-30));
        var notStarted = Goal.Create("user-2", GoalType.FatLoss, 70, DateOnly.FromDateTime(now.UtcDateTime), null, now.AddDays(-1));
        dbContext.Users.Add(User.Create("user-2", "Grace", now.AddDays(-60)));
        dbContext.Goals.AddRange(reached, notStarted);
        dbContext.GoalProgress.Add(GoalProgress.Create(reached.Id, DateOnly.FromDateTime(now.AddDays(-30).UtcDateTime), 75, 0, null, null, now.AddDays(-30)));
        dbContext.HealthMetrics.Add(HealthMetric.Create(Guid.NewGuid(), "user-1", "body_mass", 69.8, "kg", now.AddDays(-1), now, "apple_health", "w1", "d"));
        dbContext.HealthMetrics.Add(HealthMetric.Create(Guid.NewGuid(), "user-2", "body_mass", 69.8, "kg", now.AddHours(-1), now, "apple_health", "w2", "d"));
        await dbContext.SaveChangesAsync();
        var repository = new ProductAnalyticsRepository(dbContext);

        var completedForUser1 = await repository.CompleteReachedGoalsAsync("user-1", now, CancellationToken.None);
        var completedForUser2 = await repository.CompleteReachedGoalsAsync("user-2", now, CancellationToken.None);

        Assert.Equal(1, completedForUser1);
        Assert.Equal(0, completedForUser2); // no starting weight recorded: not evaluable yet
        Assert.Equal(GoalStatus.Completed, (await dbContext.Goals.SingleAsync(goal => goal.Id == reached.Id)).Status);
        Assert.Equal(GoalStatus.Active, (await dbContext.Goals.SingleAsync(goal => goal.Id == notStarted.Id)).Status);
        var recorded = await dbContext.ProductEvents.SingleAsync();
        Assert.Equal(ProductEventNames.GoalCompleted, recorded.Name);
    }
}
