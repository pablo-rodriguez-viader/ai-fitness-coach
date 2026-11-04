using System.Diagnostics.Metrics;
using AIFitnessCoach.Application.UseCases.Analytics;

namespace AIFitnessCoach.Api.Configuration;

/// <summary>
/// Runs the beta scorecard on a schedule so nobody has to call the report by hand: publishes
/// the KPIs as gauges (exported through OpenTelemetry when configured), logs a summary and logs
/// every alert as a warning. Alerts in the log aggregator can page the team.
/// Beta:KpiPublisherEnabled (default true), Beta:KpiIntervalMinutes (default 60).
/// </summary>
public sealed class BetaKpiPublisher : BackgroundService
{
    private static readonly Meter Meter = new("AIFitnessCoach.Beta");
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<BetaKpiPublisher> logger;
    private readonly TimeProvider timeProvider;
    private readonly bool enabled;
    private readonly TimeSpan interval;
    private BetaReport? latest;

    public BetaKpiPublisher(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<BetaKpiPublisher> logger,
        TimeProvider timeProvider)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.timeProvider = timeProvider;
        enabled = configuration.GetValue<bool?>("Beta:KpiPublisherEnabled") ?? true;
        var minutes = configuration.GetValue<int?>("Beta:KpiIntervalMinutes") ?? 60;
        if (minutes is < 5 or > 1440)
            throw new InvalidOperationException("Beta:KpiIntervalMinutes must be between 5 and 1440.");
        interval = TimeSpan.FromMinutes(minutes);

        Gauge("beta_active_users_daily", report => report.ActiveUsers.Daily);
        Gauge("beta_active_users_weekly", report => report.ActiveUsers.Weekly);
        Gauge("beta_active_users_monthly", report => report.ActiveUsers.Monthly);
        Gauge("beta_retention_d1", report => report.Retention.D1.Rate);
        Gauge("beta_retention_d7", report => report.Retention.D7.Rate);
        Gauge("beta_retention_d30", report => report.Retention.D30.Rate);
        Gauge("beta_workout_abandonment_ratio", report => report.Workouts.AbandonmentRate);
        Gauge("beta_coach_positive_feedback_ratio", report => report.Coach.PositiveRate);
        Gauge("beta_sync_success_ratio", report => report.HealthKit.SyncSuccessRate);
        Gauge("beta_crash_free_users_ratio", report => report.Stability.CrashFreeUsersRate);
        Gauge("beta_registered_users", report => report.Capacity.RegisteredUsers);
        Gauge("beta_remaining_seats", report => report.Capacity.RemainingSeats);
        Gauge("beta_active_alerts", report => report.Alerts.Count);
    }

    public BetaReport? Latest => latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enabled)
            return;
        try
        {
            await Task.Delay(FirstRunDelay, timeProvider, stoppingToken);
            using var timer = new PeriodicTimer(interval, timeProvider);
            do
            {
                await PublishOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Computes the last 30 days. A failure is logged and retried on the next tick.</summary>
    public async Task<BetaReport?> PublishOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var useCase = scope.ServiceProvider.GetRequiredService<GetProductAnalyticsUseCase>();
            var report = await useCase.ExecuteAsync(null, null, cancellationToken);
            if (report.Beta is not { } beta)
                return null;
            latest = beta;
            logger.LogInformation(
                "Beta KPIs: DAU {Daily}, WAU {Weekly}, MAU {Monthly}, D1 {D1}, D7 {D7}, D30 {D30}, " +
                "workouts {Started}/{Completed}/{Abandoned} (started/completed/abandoned), coach {Queries} queries " +
                "{Positive}+/{Negative}-, sync ok {SyncOk} failed {SyncFailed}, crash-free {CrashFree}, " +
                "users {Registered}/{MaxUsers}.",
                beta.ActiveUsers.Daily, beta.ActiveUsers.Weekly, beta.ActiveUsers.Monthly,
                beta.Retention.D1.Rate, beta.Retention.D7.Rate, beta.Retention.D30.Rate,
                beta.Workouts.Started, beta.Workouts.Completed, beta.Workouts.Abandoned,
                beta.Coach.Queries, beta.Coach.PositiveFeedback, beta.Coach.NegativeFeedback,
                beta.HealthKit.SyncsSucceeded, beta.HealthKit.SyncsFailed, beta.Stability.CrashFreeUsersRate,
                beta.Capacity.RegisteredUsers, beta.Capacity.MaxUsers);
            foreach (var alert in beta.Alerts)
            {
                logger.LogWarning(
                    "Beta alert {AlertCode}: {AlertMessage} (value {Value}, threshold {Threshold}).",
                    alert.Code, alert.Message, alert.Value, alert.Threshold);
            }
            return beta;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Beta KPI computation failed; retrying on the next interval.");
            return null;
        }
    }

    private void Gauge(string name, Func<BetaReport, double?> value) =>
        Meter.CreateObservableGauge(name, () =>
            latest is { } report && value(report) is { } measured
                ? [new Measurement<double>(measured)]
                : Array.Empty<Measurement<double>>());
}
