namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

/// <summary>
/// Daily totals sent by an iOS Shortcut that reads Apple Health. Used by the free personal
/// install, where the sideloaded app cannot get the HealthKit entitlement. Null means "not sent".
/// </summary>
public sealed record ShortcutHealthValues(
    double? Steps,
    double? ActiveEnergyKilocalories,
    double? RestingEnergyKilocalories,
    double? AverageHeartRate,
    double? WeightKilograms,
    double? SleepHours);

public static class ShortcutHealthImport
{
    public const string Source = "ios_shortcut";
    public const string DeviceId = "ios-shortcut";

    /// <summary>
    /// One sample per metric and day. The external id is the day, so sending the Shortcut again
    /// the same day replaces the totals instead of adding to them. Out-of-range values (a wrong
    /// unit, a typo in the Shortcut) are dropped rather than stored.
    /// </summary>
    public static IReadOnlyList<CreateHealthMetricCommand> ToMetrics(
        ShortcutHealthValues values,
        DateOnly day,
        DateTimeOffset now)
    {
        // Noon UTC falls inside the same calendar day across Europe; never later than now.
        var noon = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var observedAt = noon > now ? now : noon;
        var dayKey = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var metrics = new List<CreateHealthMetricCommand>();

        void Add(string metric, string unit, double? value, double minimum, double maximum)
        {
            if (value is not { } number || !double.IsFinite(number) || number < minimum || number > maximum)
                return;
            metrics.Add(new CreateHealthMetricCommand(
                metric, number, unit, observedAt, Source, $"{metric}:{dayKey}", DeviceId));
        }

        Add("steps", "count", values.Steps, 0, 200_000);
        Add("active_energy", "kcal", values.ActiveEnergyKilocalories, 0, 20_000);
        Add("basal_energy", "kcal", values.RestingEnergyKilocalories, 0, 20_000);
        // Zero means "no samples" for these; Shortcuts returns 0 when nothing was found.
        Add("heart_rate", "count/min", values.AverageHeartRate, 20, 250);
        Add("body_mass", "kg", values.WeightKilograms, 20, 400);
        Add("sleep_duration", "s", values.SleepHours is { } hours ? hours * 3600 : null, 60, 24 * 3600);
        return metrics;
    }
}
