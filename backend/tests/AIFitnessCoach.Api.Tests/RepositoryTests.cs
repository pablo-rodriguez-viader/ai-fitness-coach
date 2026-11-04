using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public async Task UserProfileRepository_AddsAndLoadsProfile()
    {
        await using var db = CreateContext();
        db.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var repository = new UserProfileRepository(db);
        var profile = UserProfile.Create(
            "user-1",
            "Ada",
            30,
            170,
            "maintenance",
            DateTimeOffset.UtcNow);

        await repository.SaveAsync(profile, CancellationToken.None);
        var loaded = await repository.GetByUserIdAsync("user-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Ada", loaded.DisplayName);
    }

    [Fact]
    public async Task HealthMetricRepository_ReturnsOwnerScopedStablePage()
    {
        await using var db = CreateContext();
        var profile = UserProfile.Create(
            "user-1",
            "Ada",
            30,
            170,
            "maintenance",
            DateTimeOffset.UtcNow);
        db.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        db.Users.Add(User.Create("user-2", "Grace", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        db.UserProfiles.Add(profile);
        db.UserProfiles.Add(UserProfile.Create(
            "user-2",
            "Grace",
            32,
            165,
            "maintenance",
            DateTimeOffset.UtcNow));
        var observedAt = DateTimeOffset.UtcNow;
        var first = HealthMetric.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "user-1",
            "steps",
            10,
            "count",
            observedAt,
            observedAt,
            "apple_health",
            "sample-1");
        var second = HealthMetric.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "user-1",
            "steps",
            20,
            "count",
            observedAt,
            observedAt,
            "apple_health",
            "sample-2");
        var otherUserMetric = HealthMetric.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            "user-2",
            "steps",
            30,
            "count",
            observedAt,
            observedAt,
            "apple_health",
            "sample-3");
        db.HealthMetrics.AddRange(first, second, otherUserMetric);
        await db.SaveChangesAsync();

        var repository = new HealthMetricRepository(db);
        var page = await repository.GetPageAsync(
            "user-1",
            observedAt.AddMinutes(-1),
            observedAt.AddMinutes(1),
            null,
            1,
            CancellationToken.None);
        var nextPage = await repository.GetPageAsync(
            "user-1",
            observedAt.AddMinutes(-1),
            observedAt.AddMinutes(1),
            new HealthMetricCursor(page[0].ObservedAt, page[0].Id),
            1,
            CancellationToken.None);

        Assert.Equal(second.Id, Assert.Single(page).Id);
        Assert.Equal(first.Id, Assert.Single(nextPage).Id);
    }

    [Fact]
    public async Task HealthMetricRepository_IsIdempotentForUserSourceAndRecordId()
    {
        await using var db = CreateContext();
        db.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var observedAt = DateTimeOffset.UtcNow;
        var metric = HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            "steps",
            100,
            "count",
            observedAt,
            observedAt,
            "apple_health",
            "hk-sample-123");
        var repository = new HealthMetricRepository(db);

        Assert.Equal(1, (await repository.AddBatchAsync([metric], CancellationToken.None)).InsertedCount);
        var duplicate = HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            "steps",
            100,
            "count",
            observedAt,
            observedAt,
            "apple_health",
            "hk-sample-123");
        Assert.Equal(1, (await repository.AddBatchAsync([duplicate], CancellationToken.None)).DuplicateCount);
        Assert.Equal(1, await db.HealthMetrics.CountAsync());
    }

    [Fact]
    public async Task HealthMetricRepository_UpsertsChangedSampleAndIncrementsSyncVersion()
    {
        await using var db = CreateContext();
        db.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var observedAt = DateTimeOffset.UtcNow;
        var metric = HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            "steps",
            100,
            "count",
            observedAt,
            observedAt,
            "apple_health",
            "hk-sample-123",
            "device-1");
        var repository = new HealthMetricRepository(db);
        await repository.AddBatchAsync([metric], CancellationToken.None);
        var corrected = HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            "steps",
            125,
            "count",
            observedAt.AddMinutes(1),
            observedAt.AddMinutes(2),
            "apple_health",
            "hk-sample-123",
            "device-2");

        var result = await repository.AddBatchAsync([corrected], CancellationToken.None);

        Assert.Equal(1, result.UpdatedCount);
        var stored = await db.HealthMetrics.SingleAsync();
        Assert.Equal(125, stored.Value);
        Assert.Equal("device-2", stored.DeviceId);
        Assert.Equal(2, stored.SyncVersion);
        Assert.Equal(observedAt.AddMinutes(2), stored.UpdatedAt);
    }

    [Fact]
    public async Task HealthMetricRepository_ReturnsLatestMetricsAndAggregatedDailySummaryForOwner()
    {
        await using var db = CreateContext();
        db.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        db.Users.Add(User.Create("user-2", "Grace", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        db.HealthMetrics.AddRange(
            CreateMetric("user-1", "steps", 100, "count", now, "steps-1"),
            CreateMetric("user-1", "steps", 250, "count", now.AddMinutes(1), "steps-2"),
            CreateMetric("user-1", "active_energy", 350, "kcal", now, "active-1"),
            CreateMetric("user-1", "basal_energy", 1200, "kcal", now, "basal-1"),
            CreateMetric("user-1", "heart_rate", 60, "count/min", now, "hr-1"),
            CreateMetric("user-1", "heart_rate", 80, "count/min", now.AddMinutes(1), "hr-2"),
            CreateMetric("user-1", "sleep_duration", 28_800, "s", now, "sleep-1"),
            CreateMetric("user-1", "body_mass", 70, "kg", now, "weight-1"),
            CreateMetric("user-2", "steps", 900, "count", now, "steps-other"));
        await db.SaveChangesAsync();
        var repository = new HealthMetricRepository(db);

        var summary = await repository.GetSummaryAsync(
            "user-1",
            now.AddHours(-1),
            now.AddHours(1),
            CancellationToken.None);
        var latest = await repository.GetLatestByMetricAsync("user-1", CancellationToken.None);

        Assert.Equal(350, summary.Steps);
        Assert.Equal(70, summary.WeightKilograms);
        Assert.Equal(350, summary.ActiveEnergyKilocalories);
        Assert.Equal(1550, summary.TotalEnergyKilocalories);
        Assert.Equal(70, summary.AverageHeartRateBeatsPerMinute);
        Assert.Equal(28_800, summary.SleepSeconds);
        Assert.DoesNotContain(latest, metric => metric.UserId == "user-2");
        Assert.Equal(6, latest.Count);
    }

    [Fact]
    public async Task HealthMetricRepository_DeletesOnlyRequestedOwnersSourceAndExternalIds()
    {
        await using var db = CreateContext();
        db.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        db.Users.Add(User.Create("user-2", "Grace", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        db.HealthMetrics.AddRange(
            CreateMetric("user-1", "steps", 100, "count", now, "sample-1"),
            CreateMetric("user-1", "heart_rate", 70, "count/min", now, "sample-2"),
            CreateMetric("user-2", "steps", 200, "count", now, "sample-1"));
        await db.SaveChangesAsync();
        var repository = new HealthMetricRepository(db);

        var removed = await repository.DeleteBatchAsync(
            "user-1",
            "apple_health",
            ["sample-1"],
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal(2, await db.HealthMetrics.CountAsync());
        Assert.Contains(await db.HealthMetrics.ToListAsync(), metric => metric.UserId == "user-2");
    }

    [Fact]
    public async Task AuthRepository_UsesAppleSubjectAsStableExternalIdentity()
    {
        await using var db = CreateContext();
        var repository = new AuthRepository(db);
        var identity = new AIFitnessCoach.Application.Abstractions.Persistence.AppleIdentity(
            "apple-subject-123",
            "private@example.com",
            true);
        var firstUserId = await repository.GetOrCreateAppleUserAsync(
            identity,
            "Ada",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var secondUserId = await repository.GetOrCreateAppleUserAsync(
            identity,
            null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(firstUserId, secondUserId);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.UserIdentities.ToListAsync());
    }

    [Fact]
    public void HealthMetricCursorPredicate_TranslatesForPostgres()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var db = new FitnessDbContext(options);
        var cursor = new HealthMetricCursor(DateTimeOffset.UtcNow, Guid.NewGuid());

        var sql = db.HealthMetrics
            .Where(metric => metric.ObservedAt < cursor.ObservedAt
                || (metric.ObservedAt == cursor.ObservedAt
                    && metric.Id.CompareTo(cursor.Id) < 0))
            .OrderByDescending(metric => metric.ObservedAt)
            .ThenByDescending(metric => metric.Id)
            .Take(101)
            .ToQueryString();

        Assert.Contains("observed_at", sql);
        Assert.Contains("health_metrics", sql);
    }

    [Fact]
    public void InitialSchemaMigration_IsRegisteredWithTheInfrastructureContext()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var db = new FitnessDbContext(options);

        Assert.Contains(
            "20261015100000_InitialHealthData",
            db.Database.GetMigrations());
        Assert.Contains(
            "20261018180000_AddIdentityAndHealthSync",
            db.Database.GetMigrations());
        Assert.Contains(
            "20261018200000_AddReliableHealthSync",
            db.Database.GetMigrations());
    }

    private static FitnessDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static HealthMetric CreateMetric(
        string userId,
        string metric,
        double value,
        string unit,
        DateTimeOffset observedAt,
        string externalId) =>
        HealthMetric.Create(
            Guid.NewGuid(),
            userId,
            metric,
            value,
            unit,
            observedAt,
            observedAt,
            "apple_health",
            externalId,
            "device-1");
}
