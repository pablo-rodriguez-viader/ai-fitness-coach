namespace AIFitnessCoach.Application.UseCases.Analytics;

/// <summary>A product event as the analyzers see it.</summary>
public sealed record AnalyticsEvent(
    string UserId,
    string Name,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string> Properties,
    string? AppVersion = null)
{
    public string? Property(string key) => Properties.TryGetValue(key, out var value) ? value : null;
}

/// <summary>A planned workout day and when (if ever) the user completed it.</summary>
public sealed record WorkoutDayFact(
    Guid WorkoutDayId,
    DateOnly PlannedOn,
    string Title,
    DateTimeOffset? CompletedAt);

public sealed record GoalFact(
    Guid GoalId,
    string Type,
    string Status,
    DateOnly StartsOn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RecommendationFact(
    Guid RecommendationId,
    string UserId,
    string Type,
    DateTimeOffset CreatedAt);

/// <summary>Everything the analyzers need about one user, loaded in bulk by the repository.</summary>
public sealed record UserActivitySnapshot(
    string UserId,
    IReadOnlyList<AnalyticsEvent> Events,
    IReadOnlyList<WorkoutDayFact> WorkoutDays,
    IReadOnlyList<DateOnly> NutritionLogDates,
    IReadOnlyList<GoalFact> Goals,
    IReadOnlyDictionary<DateOnly, double> DailySteps)
{
    public static UserActivitySnapshot Empty(string userId) =>
        new(userId, [], [], [], [], new Dictionary<DateOnly, double>());
}

/// <summary>Day bucketing shared by every analyzer. Product-wide reports use UTC; user-facing
/// summaries pass the device's UTC offset so "today" matches the user's calendar.</summary>
public static class AnalyticsCalendar
{
    public static DateOnly Day(DateTimeOffset moment, TimeSpan utcOffset) =>
        DateOnly.FromDateTime(moment.ToOffset(utcOffset).DateTime);

    public static IEnumerable<DateOnly> Range(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
            yield return day;
    }

    /// <summary>Validates a client-supplied offset; real offsets are within ±14 hours.</summary>
    public static TimeSpan Offset(int? utcOffsetMinutes) =>
        utcOffsetMinutes is { } minutes && minutes is >= -14 * 60 and <= 14 * 60
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.Zero;
}
