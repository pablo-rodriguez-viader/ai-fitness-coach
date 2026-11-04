using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Analytics;

public sealed record DailyActiveUsers(DateOnly Date, int Users);

public sealed record ProductMetrics(
    DateOnly From,
    DateOnly To,
    int TotalUsers,
    IReadOnlyList<DailyActiveUsers> Daily,
    double AverageDailyActiveUsers,
    int WeeklyActiveUsers,
    double? Stickiness,
    int Sessions,
    double? SessionsPerActiveUser,
    int WorkoutsStarted,
    int WorkoutsCompleted,
    int FeedbackSubmitted,
    int CoachQueries,
    int GoalsCreated,
    int GoalsCompleted,
    int MealsLogged);

/// <summary>Usage counters for a period. A user is active on a day when they produced any event.</summary>
public sealed class ProductMetricsCalculator
{
    public ProductMetrics Calculate(IReadOnlyList<AnalyticsEvent> events, DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentException("The period end must not precede its start.", nameof(to));
        var inRange = events
            .Where(item => Day(item) >= from && Day(item) <= to)
            .ToArray();
        var activeByDay = inRange
            .GroupBy(Day)
            .ToDictionary(group => group.Key, group => group.Select(item => item.UserId).Distinct().Count());
        var daily = AnalyticsCalendar.Range(from, to)
            .Select(day => new DailyActiveUsers(day, activeByDay.GetValueOrDefault(day)))
            .ToArray();
        var weekStart = to.AddDays(-6) < from ? from : to.AddDays(-6);
        var weeklyActive = inRange
            .Where(item => Day(item) >= weekStart)
            .Select(item => item.UserId)
            .Distinct()
            .Count();
        var averageDaily = daily.Length == 0 ? 0 : Math.Round(daily.Average(item => item.Users), 2);
        var activeUsers = inRange.Select(item => item.UserId).Distinct().Count();
        var sessions = Count(inRange, ProductEventNames.SessionStart);

        return new ProductMetrics(
            from,
            to,
            activeUsers,
            daily,
            averageDaily,
            weeklyActive,
            weeklyActive == 0 ? null : Math.Round(daily.TakeLast(7).Average(item => item.Users) / weeklyActive, 3),
            sessions,
            activeUsers == 0 ? null : Math.Round(sessions / (double)activeUsers, 2),
            Count(inRange, ProductEventNames.WorkoutStarted),
            // The same day can be completed via feedback and via "complete": count it once.
            CountDistinct(inRange, ProductEventNames.WorkoutCompleted, "workout_day_id"),
            Count(inRange, ProductEventNames.WorkoutFeedbackSubmitted),
            // A retried Coach question keeps its client_request_id.
            CountDistinct(inRange, ProductEventNames.CoachQuery, "client_request_id"),
            Count(inRange, ProductEventNames.GoalCreated),
            Count(inRange, ProductEventNames.GoalCompleted),
            Count(inRange, ProductEventNames.MealLogged));
    }

    private static DateOnly Day(AnalyticsEvent item) => AnalyticsCalendar.Day(item.OccurredAt, TimeSpan.Zero);

    private static int Count(IEnumerable<AnalyticsEvent> events, string name) =>
        events.Count(item => item.Name == name);

    /// <summary>Counts events once per (user, key); events without the key always count.</summary>
    private static int CountDistinct(IEnumerable<AnalyticsEvent> events, string name, string key)
    {
        var matching = events.Where(item => item.Name == name).ToArray();
        var keyed = matching
            .Where(item => item.Property(key) is not null)
            .Select(item => (item.UserId, item.Property(key)))
            .Distinct()
            .Count();
        return keyed + matching.Count(item => item.Property(key) is null);
    }
}
