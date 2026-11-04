using AIFitnessCoach.Application.Abstractions.Persistence;

namespace AIFitnessCoach.Api.Configuration;

/// <summary>
/// Applies the retention policy once a day (GDPR storage limitation) so old analytics events,
/// support reports, AI context snapshots and ended sessions disappear without manual work.
/// Retention:ProductEventsDays (395: 13 months, enough for a year of retention cohorts),
/// Retention:SupportReportsDays (730), Retention:AIContextSnapshotsDays (90),
/// Retention:EndedSessionsDays (30), Retention:Enabled (true).
/// </summary>
public sealed class DataRetentionService : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DataRetentionService> logger;
    private readonly TimeProvider timeProvider;
    private readonly bool enabled;

    public DataRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataRetentionService> logger,
        TimeProvider timeProvider)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.timeProvider = timeProvider;
        enabled = configuration.GetValue<bool?>("Retention:Enabled") ?? true;
        Policy = ReadPolicy(configuration);
    }

    public RetentionPolicy Policy { get; }

    public static RetentionPolicy ReadPolicy(IConfiguration configuration)
    {
        static int Days(IConfiguration configuration, string key, int fallback, int minimum)
        {
            var value = configuration.GetValue<int?>($"Retention:{key}") ?? fallback;
            if (value < minimum || value > 3650)
                throw new InvalidOperationException($"Retention:{key} must be between {minimum} and 3650 days.");
            return value;
        }

        return new RetentionPolicy(
            Days(configuration, "ProductEventsDays", 395, 60),
            Days(configuration, "SupportReportsDays", 730, 30),
            Days(configuration, "AIContextSnapshotsDays", 90, 1),
            Days(configuration, "EndedSessionsDays", 30, 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enabled)
            return;
        try
        {
            await Task.Delay(FirstRunDelay, timeProvider, stoppingToken);
            using var timer = new PeriodicTimer(Interval, timeProvider);
            do
            {
                await PurgeOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<RetentionResult?> PurgeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDataRetentionStore>();
            var result = await store.PurgeAsync(Policy, timeProvider.GetUtcNow(), cancellationToken);
            logger.LogInformation(
                "Retention applied: {Events} events, {Reports} support reports, {Snapshots} AI snapshots, {Sessions} ended sessions deleted.",
                result.ProductEvents, result.SupportReports, result.AIContextSnapshots, result.EndedSessions);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Retention purge failed; retrying tomorrow.");
            return null;
        }
    }
}
