using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;

namespace AIFitnessCoach.Application.UseCases.Analytics;

/// <summary>Streak, achievements, weekly summary, adherence and reminders for the signed-in user.</summary>
public sealed class GetEngagementSummaryUseCase(
    IProductAnalyticsRepository repository,
    MotivationEngine motivationEngine,
    TimeProvider timeProvider)
{
    /// <summary>History needed for 30-day streaks plus the previous week of comparisons.</summary>
    public const int HistoryDays = 90;

    public async Task<EngagementSummary> ExecuteAsync(
        string userId,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "An authenticated user is required.");
        var now = timeProvider.GetUtcNow();
        await repository.CompleteReachedGoalsAsync(userId, now, cancellationToken);
        var snapshots = await repository.GetSnapshotsAsync([userId], now.AddDays(-HistoryDays), cancellationToken);
        var snapshot = snapshots.FirstOrDefault() ?? UserActivitySnapshot.Empty(userId);
        return motivationEngine.Build(snapshot, now, AnalyticsCalendar.Offset(utcOffsetMinutes));
    }
}

public sealed record AdherenceOverview(
    int UsersMeasured,
    double? AverageTraining,
    double? AverageNutrition,
    double? AverageGoals);

public sealed record DropoutOverview(
    int UsersAnalyzed,
    int Low,
    int Medium,
    int High,
    IReadOnlyList<DropoutRisk> AtRisk);

public sealed record ProductAnalyticsReport(
    DateTimeOffset GeneratedAt,
    ProductMetrics Metrics,
    RetentionReport Retention,
    AdherenceOverview Adherence,
    DropoutOverview Dropout,
    ProductInsightsReport Insights,
    BetaReport? Beta = null);

/// <summary>Private-beta settings: `Beta:MaxUsers` caps sign-ups (null = no cap).</summary>
public sealed record BetaSettings(int? MaxUsers);

/// <summary>Product-wide report for the team running the beta.</summary>
public sealed class GetProductAnalyticsUseCase(
    IProductAnalyticsRepository repository,
    ProductMetricsCalculator metricsCalculator,
    RetentionAnalyzer retentionAnalyzer,
    AdherenceEngine adherenceEngine,
    DropoutRiskAnalyzer dropoutRiskAnalyzer,
    ProductInsights productInsights,
    BetaReadinessAnalyzer betaAnalyzer,
    BetaSettings betaSettings,
    TimeProvider timeProvider)
{
    public const int MaximumRangeDays = 120;
    /// <summary>Retention needs each user's first day, so history is loaded further back.</summary>
    public const int RetentionHistoryDays = 365;

    public async Task<ProductAnalyticsReport> ExecuteAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var end = to ?? DateOnly.FromDateTime(now.UtcDateTime);
        var start = from ?? end.AddDays(-29);
        if (start > end || end.DayNumber - start.DayNumber + 1 > MaximumRangeDays)
            throw UseCaseValidationException.For("from", $"The range must be 1 to {MaximumRangeDays} days.");

        var endInstant = new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var historyStart = endInstant.AddDays(-RetentionHistoryDays);
        var events = await repository.GetEventsAsync(historyStart, endInstant, cancellationToken);

        var metrics = metricsCalculator.Calculate(events, start, end);
        var retention = retentionAnalyzer.Analyze(events, end);

        // Adherence and dropout look at users active in the period; snapshots cover 4 weeks back.
        var periodUsers = events
            .Where(item => AnalyticsCalendar.Day(item.OccurredAt, TimeSpan.Zero) >= start
                && AnalyticsCalendar.Day(item.OccurredAt, TimeSpan.Zero) <= end)
            .Select(item => item.UserId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var snapshotStart = new DateTimeOffset(start.AddDays(-14).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var snapshots = periodUsers.Length == 0
            ? []
            : await repository.GetSnapshotsAsync(periodUsers, snapshotStart, cancellationToken);

        var adherenceReports = snapshots
            .Select(snapshot => adherenceEngine.Calculate(snapshot, start, end, TimeSpan.Zero))
            .ToArray();
        var adherence = new AdherenceOverview(
            adherenceReports.Length,
            Average(adherenceReports.Select(report => report.Training.Rate)),
            Average(adherenceReports.Select(report => report.Nutrition.Rate)),
            Average(adherenceReports.Select(report => report.Goals.Rate)));

        var risks = snapshots.Select(snapshot => dropoutRiskAnalyzer.Analyze(snapshot, end)).ToArray();
        var dropout = new DropoutOverview(
            risks.Length,
            risks.Count(risk => risk.Level == DropoutRiskLevel.Low),
            risks.Count(risk => risk.Level == DropoutRiskLevel.Medium),
            risks.Count(risk => risk.Level == DropoutRiskLevel.High),
            risks.Where(risk => risk.Level != DropoutRiskLevel.Low)
                .OrderByDescending(risk => risk.Score)
                .Take(50)
                .ToArray());

        var periodStartInstant = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var recommendations = await repository.GetRecommendationsAsync(periodStartInstant, endInstant, cancellationToken);
        var completions = await repository.GetCompletedWorkoutsAsync(periodStartInstant, cancellationToken);
        var insights = productInsights.Analyze(events, recommendations, completions, start, end);

        var passive = await repository.GetPassiveEventsAsync(periodStartInstant, endInstant, cancellationToken);
        var registeredUsers = await repository.CountUsersAsync(cancellationToken);
        var beta = betaAnalyzer.Analyze(
            events, passive, retention, start, end, now, registeredUsers, betaSettings.MaxUsers);

        return new ProductAnalyticsReport(now, metrics, retention, adherence, dropout, insights, beta);
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var measured = values.Where(value => value is not null).Select(value => value!.Value).ToArray();
        return measured.Length == 0 ? null : Math.Round(measured.Average(), 3);
    }
}
