using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Application.UseCases.Profiles;
using AIFitnessCoach.Domain.Entities;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class UseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateUserProfileUseCase_CreatesAndReturnsProfile()
    {
        var repository = new FakeUserProfileRepository();
        var useCase = new CreateUserProfileUseCase(repository, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(
            "user-1",
            new CreateUserProfileCommand("Ada", 30, 170, "maintenance"),
            CancellationToken.None);

        Assert.Equal("user-1", result.UserId);
        Assert.Equal("Ada", result.DisplayName);
        Assert.Equal(Now, result.CreatedAt);
        Assert.Same(repository.Profile, await repository.GetByUserIdAsync("user-1", default));
    }

    [Fact]
    public async Task CreateUserProfileUseCase_RejectsInvalidProfile()
    {
        var useCase = new CreateUserProfileUseCase(
            new FakeUserProfileRepository(),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new CreateUserProfileCommand(null, 30, 170, "maintenance"),
                CancellationToken.None));

        Assert.Contains("displayName", exception.Errors.Keys);
    }

    [Fact]
    public async Task GetUserProfileUseCase_ReturnsNullWhenProfileDoesNotExist()
    {
        var useCase = new GetUserProfileUseCase(new FakeUserProfileRepository());

        var result = await useCase.ExecuteAsync("missing-user", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateHealthMetricUseCase_RequiresAnExistingUser()
    {
        var repository = new FakeHealthMetricRepository();
        var useCase = new CreateHealthMetricUseCase(repository, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new CreateHealthMetricCommand("steps", 120, "count", Now, "apple_health", "sample-1"),
                CancellationToken.None));

        Assert.Empty(repository.Metrics);
    }

    [Fact]
    public async Task CreateHealthMetricUseCase_CreatesMetricForOwner()
    {
        var repository = new FakeHealthMetricRepository { HasUser = true };
        var useCase = new CreateHealthMetricUseCase(repository, new FixedTimeProvider(Now));

        var metric = await useCase.ExecuteAsync(
            "user-1",
            new CreateHealthMetricCommand("steps", 120, "count", Now, "apple_health", "sample-1"),
            CancellationToken.None);

        Assert.Equal("user-1", metric.UserId);
        Assert.Same(metric, Assert.Single(repository.Metrics));
    }

    [Fact]
    public async Task GetHealthMetricsUseCase_RejectsInvalidPageSize()
    {
        var useCase = new GetHealthMetricsUseCase(
            new FakeHealthMetricRepository(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                null,
                null,
                null,
                501,
                CancellationToken.None));
    }

    [Fact]
    public async Task SyncHealthMetricsUseCase_RequiresStableSourceIdsAndDeduplicatesBatch()
    {
        var repository = new FakeHealthMetricRepository { HasUser = true };
        var useCase = new SyncHealthMetricsUseCase(repository, new FixedTimeProvider(Now));
        var duplicate = new CreateHealthMetricCommand(
            "steps",
            120,
            "count",
            Now,
            "apple_health",
            "sample-1",
            "device-1");

        var inserted = await useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand([duplicate, duplicate]),
            CancellationToken.None);

        Assert.Equal(1, inserted.InsertedCount);
        Assert.Equal(1, inserted.DuplicateCount);
        Assert.Single(repository.Metrics);
    }

    [Fact]
    public async Task SyncHealthMetricsUseCase_RejectsMissingSourceRecordId()
    {
        var useCase = new SyncHealthMetricsUseCase(
            new FakeHealthMetricRepository { HasUser = true },
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand([
                new CreateHealthMetricCommand("steps", 1, "count", Now, "apple_health", null, "device-1")
            ]),
            CancellationToken.None));
    }

    [Fact]
    public async Task SyncHealthMetricsUseCase_RejectsMissingDeviceId()
    {
        var useCase = new SyncHealthMetricsUseCase(
            new FakeHealthMetricRepository { HasUser = true },
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand([
                new CreateHealthMetricCommand("steps", 1, "count", Now, "apple_health", "sample-1")
            ]),
            CancellationToken.None));

        Assert.Contains("metrics[0].deviceId", exception.Errors.Keys);
    }

    [Fact]
    public async Task SyncHealthMetricsUseCase_RejectsFutureTimestamp()
    {
        var useCase = new SyncHealthMetricsUseCase(
            new FakeHealthMetricRepository { HasUser = true },
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand([
                new CreateHealthMetricCommand(
                    "steps",
                    1,
                    "count",
                    Now.AddMinutes(6),
                    "apple_health",
                    "sample-1",
                    "device-1")
            ]),
            CancellationToken.None));

        Assert.Contains("metrics[0].observedAt", exception.Errors.Keys);
    }

    [Fact]
    public async Task SyncHealthMetricsUseCase_RejectsUnitThatDoesNotMatchMetric()
    {
        var useCase = new SyncHealthMetricsUseCase(
            new FakeHealthMetricRepository { HasUser = true },
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(
            "user-1",
            new SyncHealthMetricsCommand([
                new CreateHealthMetricCommand(
                    "steps",
                    1,
                    "kg",
                    Now,
                    "apple_health",
                    "sample-1",
                    "device-1")
            ]),
            CancellationToken.None));

        Assert.Contains("metrics[0]", exception.Errors.Keys);
    }

    [Fact]
    public async Task GetHealthSummaryUseCase_RejectsRangeLongerThanTwoDays()
    {
        var useCase = new GetHealthSummaryUseCase(
            new FakeHealthMetricRepository { HasUser = true },
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(
            "user-1",
            Now.AddDays(-3),
            Now,
            CancellationToken.None));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeUserProfileRepository : IUserProfileRepository
    {
        public UserProfile? Profile { get; private set; }

        public Task<UserProfile?> GetByUserIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(Profile?.UserId == userId ? Profile : null);

        public Task SaveAsync(UserProfile profile, CancellationToken cancellationToken)
        {
            Profile = profile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHealthMetricRepository : IHealthMetricRepository
    {
        public bool HasUser { get; init; }
        public List<HealthMetric> Metrics { get; } = [];

        public Task AddAsync(HealthMetric metric, CancellationToken cancellationToken)
        {
            Metrics.Add(metric);
            return Task.CompletedTask;
        }

        public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(HasUser);

        public Task<HealthMetricBatchResult> AddBatchAsync(
            IReadOnlyList<HealthMetric> metrics,
            CancellationToken cancellationToken)
        {
            var inserted = 0;
            var updated = 0;
            foreach (var metric in metrics)
            {
                var existing = Metrics.FirstOrDefault(existing =>
                    existing.UserId == metric.UserId
                    && existing.Source == metric.Source
                    && existing.ExternalId == metric.ExternalId);
                if (existing is null)
                {
                    Metrics.Add(metric);
                    inserted++;
                }
                else if (existing.UpdateFromSync(
                    metric.Value,
                    metric.Unit,
                    metric.ObservedAt,
                    metric.DeviceId,
                    metric.UpdatedAt))
                {
                    updated++;
                }
            }

            return Task.FromResult(new HealthMetricBatchResult(
                inserted,
                updated,
                metrics.Count - inserted - updated));
        }

        public Task<int> DeleteBatchAsync(
            string userId,
            string source,
            IReadOnlyList<string> externalIds,
            CancellationToken cancellationToken)
        {
            var removed = Metrics.RemoveAll(metric =>
                metric.UserId == userId
                && metric.Source == source
                && externalIds.Contains(metric.ExternalId));
            return Task.FromResult(removed);
        }

        public Task<IReadOnlyList<HealthMetric>> GetPageAsync(
            string userId,
            DateTimeOffset from,
            DateTimeOffset to,
            HealthMetricCursor? before,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HealthMetric>>([]);

        public Task<IReadOnlyList<HealthMetric>> GetLatestByMetricAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HealthMetric>>([]);

        public Task<HealthMetricSummary> GetSummaryAsync(
            string userId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HealthMetricSummary(null, null, null, null, null, null));
    }
}
