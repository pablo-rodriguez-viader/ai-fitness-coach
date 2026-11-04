using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Analytics;

public sealed record BetaActiveUsers(int Daily, double AverageDaily, int Weekly, int Monthly, double? DailyOverMonthly);

public sealed record BetaRetentionPoint(int EligibleUsers, int RetainedUsers, double? Rate);

public sealed record BetaRetention(BetaRetentionPoint D1, BetaRetentionPoint D7, BetaRetentionPoint D30);

public sealed record BetaWorkouts(int Started, int Completed, int Abandoned, int InProgress, double? AbandonmentRate);

public sealed record BetaCoach(
    int Queries,
    int CoachQueries,
    int AnalysisQueries,
    int UsersAsking,
    int RepeatUsers,
    int RepeatedQueries,
    int DuplicateQuestions,
    int PositiveFeedback,
    int NegativeFeedback,
    double? PositiveRate);

public sealed record BetaHealthKit(
    int SyncsSucceeded,
    int SyncsFailed,
    int SyncsDeadLettered,
    double? SyncSuccessRate,
    int UsersSyncing,
    int UsersWithSyncFailures,
    int UsersConnected,
    int UsersPermissionLikelyDenied,
    int AuthorizationFailures);

public sealed record CountByKey(string Key, int Count);

public sealed record BuildStability(string AppVersion, int Users, int Crashes, int Hangs, int Errors);

public sealed record BetaStability(
    int ActiveUsers,
    int UsersWithCrashes,
    double? CrashFreeUsersRate,
    int Crashes,
    int Hangs,
    int Errors,
    IReadOnlyList<CountByKey> ErrorsByArea,
    IReadOnlyList<BuildStability> ByBuild);

public sealed record BetaCapacity(int RegisteredUsers, int? MaxUsers, int? RemainingSeats);

public sealed record BetaAlert(string Code, string Message, double Value, double Threshold);

public sealed record BetaReport(
    BetaActiveUsers ActiveUsers,
    BetaRetention Retention,
    BetaWorkouts Workouts,
    BetaCoach Coach,
    BetaHealthKit HealthKit,
    BetaStability Stability,
    BetaCapacity Capacity,
    IReadOnlyList<BetaAlert> Alerts);

/// <summary>
/// The private-beta scorecard: who uses the app, whether they come back, whether workouts are
/// finished, whether the Coach helps, whether HealthKit sync works and whether the app is stable.
/// Alerts flag thresholds so the beta can run without someone reading every number.
/// </summary>
public sealed class BetaReadinessAnalyzer
{
    /// <summary>A started workout with no completion this long after it counts as abandoned.</summary>
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromHours(24);

    // Alert thresholds (and the minimum sample before an alert is meaningful).
    public const double MinimumCrashFreeUsers = 0.99;
    public const double MinimumSyncSuccess = 0.95;
    public const int MinimumSyncSample = 20;
    public const double MaximumAbandonment = 0.5;
    public const int MinimumWorkoutSample = 10;
    public const double MinimumCoachPositive = 0.7;
    public const int MinimumFeedbackSample = 10;
    public const double MaximumPermissionDenied = 0.2;
    public const int MinimumConnectedSample = 5;
    public const double CapacityWarning = 0.9;

    public BetaReport Analyze(
        IReadOnlyList<AnalyticsEvent> activityEvents,
        IReadOnlyList<AnalyticsEvent> passiveEvents,
        RetentionReport retention,
        DateOnly from,
        DateOnly to,
        DateTimeOffset now,
        int registeredUsers,
        int? maxUsers)
    {
        var inPeriod = activityEvents.Where(item => InRange(item, from, to)).ToArray();
        var passiveInPeriod = passiveEvents.Where(item => InRange(item, from, to)).ToArray();
        var throughEnd = activityEvents.Where(item => Day(item) <= to).ToArray();
        var asOf = Min(now, new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));

        var activeUsers = ActiveUsers(throughEnd, inPeriod, from, to);
        var workouts = Workouts(inPeriod, throughEnd, asOf);
        var coach = Coach(inPeriod);
        var healthKit = HealthKit(inPeriod, passiveInPeriod, throughEnd);
        var stability = Stability(inPeriod, passiveInPeriod);
        var capacity = new BetaCapacity(
            registeredUsers,
            maxUsers,
            maxUsers is { } max ? Math.Max(0, max - registeredUsers) : null);
        var report = new BetaReport(
            activeUsers,
            new BetaRetention(Point(retention, 1), Point(retention, 7), Point(retention, 30)),
            workouts,
            coach,
            healthKit,
            stability,
            capacity,
            []);
        return report with { Alerts = Alerts(report) };
    }

    private static BetaActiveUsers ActiveUsers(
        IReadOnlyList<AnalyticsEvent> throughEnd,
        IReadOnlyList<AnalyticsEvent> inPeriod,
        DateOnly from,
        DateOnly to)
    {
        int UniqueSince(DateOnly start) => throughEnd
            .Where(item => Day(item) >= start)
            .Select(item => item.UserId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var daily = UniqueSince(to);
        var monthly = UniqueSince(to.AddDays(-29));
        var days = to.DayNumber - from.DayNumber + 1;
        var averageDaily = days <= 0
            ? 0
            : Math.Round(inPeriod
                .GroupBy(Day)
                .Sum(group => group.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count()) / (double)days, 2);
        return new BetaActiveUsers(
            daily,
            averageDaily,
            UniqueSince(to.AddDays(-6)),
            monthly,
            monthly == 0 ? null : Math.Round(averageDaily / monthly, 3));
    }

    private static BetaWorkouts Workouts(
        IReadOnlyList<AnalyticsEvent> inPeriod,
        IReadOnlyList<AnalyticsEvent> throughEnd,
        DateTimeOffset asOf)
    {
        var starts = inPeriod
            .Where(item => item.Name == ProductEventNames.WorkoutStarted)
            .GroupBy(item => (item.UserId, Day: item.Property("workout_day_id") ?? Guid.NewGuid().ToString()))
            .Select(group => group.OrderBy(item => item.OccurredAt).First())
            .ToArray();
        var completions = throughEnd
            .Where(item => item.Name == ProductEventNames.WorkoutCompleted && item.Property("workout_day_id") is not null)
            .GroupBy(item => (item.UserId, Day: item.Property("workout_day_id")!))
            .ToDictionary(group => group.Key, group => group.Select(item => item.OccurredAt).ToArray());
        var completedInPeriod = inPeriod
            .Where(item => item.Name == ProductEventNames.WorkoutCompleted)
            .Select(item => (item.UserId, item.Property("workout_day_id") ?? Guid.NewGuid().ToString()))
            .Distinct()
            .Count();

        var abandoned = 0;
        var inProgress = 0;
        var evaluated = 0;
        foreach (var start in starts)
        {
            var key = (start.UserId, start.Property("workout_day_id") ?? string.Empty);
            // Completing the day shortly before opening it again still counts as finished.
            var finished = completions.TryGetValue(key, out var times)
                && times.Any(time => time >= start.OccurredAt.AddHours(-12) && time <= start.OccurredAt + AbandonAfter);
            if (finished)
            {
                evaluated++;
                continue;
            }
            if (asOf - start.OccurredAt < AbandonAfter)
            {
                inProgress++;
                continue;
            }
            evaluated++;
            abandoned++;
        }
        return new BetaWorkouts(
            starts.Length,
            completedInPeriod,
            abandoned,
            inProgress,
            evaluated == 0 ? null : Math.Round(abandoned / (double)evaluated, 3));
    }

    private static BetaCoach Coach(IReadOnlyList<AnalyticsEvent> inPeriod)
    {
        // A retried request keeps its client_request_id: count it once.
        var queries = inPeriod
            .Where(item => item.Name == ProductEventNames.CoachQuery)
            .GroupBy(item => (item.UserId, item.Property("client_request_id") ?? Guid.NewGuid().ToString()))
            .Select(group => group.OrderBy(item => item.OccurredAt).First())
            .OrderBy(item => item.OccurredAt)
            .ToArray();
        var perUser = queries.GroupBy(item => item.UserId, StringComparer.Ordinal).ToArray();
        var duplicates = perUser.Sum(group =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return group.Count(item => item.Property("question_hash") is { } hash && !seen.Add(hash));
        });
        var feedback = inPeriod.Where(item => item.Name == ProductEventNames.CoachFeedback).ToArray();
        var positive = feedback.Count(item => item.Property("rating") == "positive");
        var negative = feedback.Count(item => item.Property("rating") == "negative");
        return new BetaCoach(
            queries.Length,
            queries.Count(item => item.Property("kind") != "analysis"),
            queries.Count(item => item.Property("kind") == "analysis"),
            perUser.Length,
            perUser.Count(group => group.Count() >= 2),
            queries.Length - perUser.Length,
            duplicates,
            positive,
            negative,
            positive + negative == 0 ? null : Math.Round(positive / (double)(positive + negative), 3));
    }

    private static BetaHealthKit HealthKit(
        IReadOnlyList<AnalyticsEvent> inPeriod,
        IReadOnlyList<AnalyticsEvent> passiveInPeriod,
        IReadOnlyList<AnalyticsEvent> throughEnd)
    {
        var succeeded = passiveInPeriod.Where(item => item.Name == ProductEventNames.SyncCompleted).ToArray();
        var failed = passiveInPeriod.Where(item => item.Name == ProductEventNames.SyncFailed).ToArray();
        var deadLettered = passiveInPeriod.Count(item => item.Name == ProductEventNames.SyncDeadLettered);
        var connected = throughEnd
            .Where(item => item.Name == ProductEventNames.HealthConnected)
            .Select(item => item.UserId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        // iOS never says which read permissions were denied; a Health check that finds no data
        // in any requested type is the closest signal ("likely denied or no data").
        var likelyDenied = throughEnd
            .Where(item => item.Name == ProductEventNames.HealthKitDataCheck)
            .GroupBy(item => item.UserId, StringComparer.Ordinal)
            .Count(group => group.OrderBy(item => item.OccurredAt).Last().Property("readable_types") == "0");
        return new BetaHealthKit(
            succeeded.Length,
            failed.Length,
            deadLettered,
            succeeded.Length + failed.Length == 0
                ? null
                : Math.Round(succeeded.Length / (double)(succeeded.Length + failed.Length), 3),
            succeeded.Concat(failed).Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count(),
            failed.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count(),
            connected,
            likelyDenied,
            inPeriod.Count(item => item.Name == ProductEventNames.HealthKitAuthorization
                && item.Property("outcome") == "failed"));
    }

    private static BetaStability Stability(
        IReadOnlyList<AnalyticsEvent> inPeriod,
        IReadOnlyList<AnalyticsEvent> passiveInPeriod)
    {
        var active = inPeriod.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count();
        var crashes = passiveInPeriod.Where(item => item.Name == ProductEventNames.CrashReport).ToArray();
        var hangs = passiveInPeriod.Where(item => item.Name == ProductEventNames.HangReport).ToArray();
        var errors = passiveInPeriod.Where(item => item.Name == ProductEventNames.Error).ToArray();
        var usersWithCrashes = crashes.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count();
        var byBuild = passiveInPeriod
            .Where(item => item.Name is ProductEventNames.CrashReport or ProductEventNames.HangReport or ProductEventNames.Error)
            .GroupBy(item => item.AppVersion ?? "unknown", StringComparer.Ordinal)
            .Select(group => new BuildStability(
                group.Key,
                group.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count(),
                group.Where(item => item.Name == ProductEventNames.CrashReport).Sum(Occurrences),
                group.Where(item => item.Name == ProductEventNames.HangReport).Sum(Occurrences),
                group.Count(item => item.Name == ProductEventNames.Error)))
            .OrderByDescending(item => item.Crashes)
            .ThenByDescending(item => item.Errors)
            .ToArray();
        return new BetaStability(
            active,
            usersWithCrashes,
            active == 0 ? null : Math.Round(1 - Math.Min(usersWithCrashes, active) / (double)active, 4),
            crashes.Sum(Occurrences),
            hangs.Sum(Occurrences),
            errors.Length,
            errors
                .GroupBy(item => item.Property("area") ?? "unknown", StringComparer.Ordinal)
                .Select(group => new CountByKey(group.Key, group.Count()))
                .OrderByDescending(item => item.Count)
                .Take(10)
                .ToArray(),
            byBuild);
    }

    public static IReadOnlyList<BetaAlert> Alerts(BetaReport report)
    {
        var alerts = new List<BetaAlert>();
        if (report.Stability.CrashFreeUsersRate is { } crashFree && crashFree < MinimumCrashFreeUsers)
            alerts.Add(new BetaAlert("crash_free_users", "Usuarios sin crashes por debajo del objetivo.", crashFree, MinimumCrashFreeUsers));
        if (report.HealthKit.SyncSuccessRate is { } sync
            && report.HealthKit.SyncsSucceeded + report.HealthKit.SyncsFailed >= MinimumSyncSample
            && sync < MinimumSyncSuccess)
            alerts.Add(new BetaAlert("sync_success", "Demasiadas sincronizaciones de Salud fallidas.", sync, MinimumSyncSuccess));
        if (report.HealthKit.SyncsDeadLettered > 0)
            alerts.Add(new BetaAlert("sync_dead_lettered", "Hay cambios de Salud descartados tras varios rechazos.", report.HealthKit.SyncsDeadLettered, 0));
        if (report.Workouts.AbandonmentRate is { } abandonment
            && report.Workouts.Abandoned + report.Workouts.Completed >= MinimumWorkoutSample
            && abandonment > MaximumAbandonment)
            alerts.Add(new BetaAlert("workout_abandonment", "Más de la mitad de los entrenamientos iniciados se abandonan.", abandonment, MaximumAbandonment));
        if (report.Coach.PositiveRate is { } positive
            && report.Coach.PositiveFeedback + report.Coach.NegativeFeedback >= MinimumFeedbackSample
            && positive < MinimumCoachPositive)
            alerts.Add(new BetaAlert("coach_feedback", "Feedback del Coach por debajo del objetivo.", positive, MinimumCoachPositive));
        if (report.HealthKit.UsersConnected >= MinimumConnectedSample)
        {
            var denied = report.HealthKit.UsersPermissionLikelyDenied / (double)report.HealthKit.UsersConnected;
            if (denied > MaximumPermissionDenied)
                alerts.Add(new BetaAlert("healthkit_permissions", "Muchos usuarios conectan Salud sin datos legibles (permisos probablemente denegados).", Math.Round(denied, 3), MaximumPermissionDenied));
        }
        if (report.Capacity.MaxUsers is { } max and > 0
            && report.Capacity.RegisteredUsers >= max * CapacityWarning)
            alerts.Add(new BetaAlert("beta_capacity", "La beta está casi completa: amplía Beta:MaxUsers para la siguiente etapa.", report.Capacity.RegisteredUsers, max));
        return alerts;
    }

    private static BetaRetentionPoint Point(RetentionReport retention, int day) =>
        retention.Classic.FirstOrDefault(point => point.Day == day) is { } point
            ? new BetaRetentionPoint(point.EligibleUsers, point.RetainedUsers, point.Rate)
            : new BetaRetentionPoint(0, 0, null);

    private static int Occurrences(AnalyticsEvent item) =>
        int.TryParse(item.Property("count"), out var count) && count is > 0 and < 1_000 ? count : 1;

    private static bool InRange(AnalyticsEvent item, DateOnly from, DateOnly to) =>
        Day(item) >= from && Day(item) <= to;

    private static DateOnly Day(AnalyticsEvent item) => AnalyticsCalendar.Day(item.OccurredAt, TimeSpan.Zero);

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}
