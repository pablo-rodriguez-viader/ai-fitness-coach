namespace AIFitnessCoach.Application.UseCases.Analytics;

/// <param name="Rate">Null when no user has been around long enough to measure this day.</param>
public sealed record RetentionPoint(int Day, int EligibleUsers, int RetainedUsers, double? Rate);

public sealed record CohortRetention(DateOnly CohortDate, int Users, IReadOnlyList<RetentionPoint> Points);

/// <param name="Classic">Share of users active exactly N days after their first day.</param>
/// <param name="Rolling">Share of users active on day N or any later day ("still around").</param>
public sealed record RetentionReport(
    DateOnly AsOf,
    IReadOnlyList<RetentionPoint> Classic,
    IReadOnlyList<RetentionPoint> Rolling,
    IReadOnlyList<CohortRetention> Cohorts);

/// <summary>
/// Day 1, 7 and 30 retention by first-activity cohort. A user only counts towards day N once
/// N days have passed since their first day, so recent sign-ups never drag the rate down.
/// </summary>
public sealed class RetentionAnalyzer
{
    public static readonly IReadOnlyList<int> Days = [1, 7, 30];

    public RetentionReport Analyze(IReadOnlyList<AnalyticsEvent> events, DateOnly asOf)
    {
        var activeDaysByUser = events
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => AnalyticsCalendar.Day(item.OccurredAt, TimeSpan.Zero))
                    .Where(day => day <= asOf)
                    .ToHashSet());
        var users = activeDaysByUser
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => new UserDays(pair.Key, pair.Value.Min(), pair.Value))
            .ToArray();

        var cohorts = users
            .GroupBy(user => user.First)
            .OrderBy(group => group.Key)
            .Select(group => new CohortRetention(
                group.Key,
                group.Count(),
                Days.Select(day => Point(group, day, asOf, rolling: false)).ToArray()))
            .ToArray();

        return new RetentionReport(
            asOf,
            Days.Select(day => Point(users, day, asOf, rolling: false)).ToArray(),
            Days.Select(day => Point(users, day, asOf, rolling: true)).ToArray(),
            cohorts);
    }

    private static RetentionPoint Point(IEnumerable<UserDays> users, int day, DateOnly asOf, bool rolling)
    {
        var eligible = users.Where(user => user.First.AddDays(day) <= asOf).ToArray();
        var retained = eligible.Count(user =>
        {
            var target = user.First.AddDays(day);
            return rolling ? user.Days.Any(active => active >= target) : user.Days.Contains(target);
        });
        return new RetentionPoint(
            day,
            eligible.Length,
            retained,
            eligible.Length == 0 ? null : Math.Round(retained / (double)eligible.Length, 3));
    }

    private sealed record UserDays(string UserId, DateOnly First, HashSet<DateOnly> Days);
}
