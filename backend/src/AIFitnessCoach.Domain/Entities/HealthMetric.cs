namespace AIFitnessCoach.Domain.Entities;

public sealed class HealthMetric
{
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedUnits =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["steps"] = new HashSet<string>(StringComparer.Ordinal) { "count" },
            ["distance_walking_running"] = new HashSet<string>(StringComparer.Ordinal) { "m" },
            ["active_energy"] = new HashSet<string>(StringComparer.Ordinal) { "kcal" },
            ["basal_energy"] = new HashSet<string>(StringComparer.Ordinal) { "kcal" },
            ["total_energy"] = new HashSet<string>(StringComparer.Ordinal) { "kcal" },
            ["heart_rate"] = new HashSet<string>(StringComparer.Ordinal) { "count/min" },
            ["oxygen_saturation"] = new HashSet<string>(StringComparer.Ordinal) { "percent" },
            ["body_mass"] = new HashSet<string>(StringComparer.Ordinal) { "kg" },
            ["body_mass_index"] = new HashSet<string>(StringComparer.Ordinal) { "count" },
            ["sleep_duration"] = new HashSet<string>(StringComparer.Ordinal) { "s" },
            ["workout_duration"] = new HashSet<string>(StringComparer.Ordinal) { "s" },
            ["workout_distance"] = new HashSet<string>(StringComparer.Ordinal) { "m" },
            ["vo2_max"] = new HashSet<string>(StringComparer.Ordinal) { "ml/kg/min" },
            ["resting_heart_rate"] = new HashSet<string>(StringComparer.Ordinal) { "count/min" },
            ["heart_rate_variability"] = new HashSet<string>(StringComparer.Ordinal) { "ms" }
        };

    private HealthMetric()
    {
        UserId = string.Empty;
        Metric = string.Empty;
        Unit = string.Empty;
        Source = string.Empty;
        ExternalId = string.Empty;
        DeviceId = string.Empty;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public string Metric { get; private set; }
    public double Value { get; private set; }
    public string Unit { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string Source { get; private set; }
    public string ExternalId { get; private set; }
    public string DeviceId { get; private set; }
    public int SyncVersion { get; private set; }
    public User User { get; private set; } = null!;

    public static HealthMetric Create(
        Guid id,
        string userId,
        string metric,
        double value,
        string unit,
        DateTimeOffset observedAt,
        DateTimeOffset createdAt,
        string source = "legacy",
        string? externalId = null,
        string deviceId = "legacy")
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A metric id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(metric)
            || !AllowedUnits.TryGetValue(metric, out var units)
            || string.IsNullOrWhiteSpace(unit)
            || !units.Contains(unit))
            throw new ArgumentException("Metric or unit is not supported.", nameof(metric));
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be finite and non-negative.");
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 64)
            throw new ArgumentException("A valid source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Trim().Length > 128)
            throw new ArgumentException("A valid external id is required.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Trim().Length > 128)
            throw new ArgumentException("A valid device id is required.", nameof(deviceId));

        return new HealthMetric
        {
            Id = id,
            UserId = userId.Trim(),
            Metric = metric,
            Value = value,
            Unit = unit,
            ObservedAt = observedAt.ToUniversalTime(),
            CreatedAt = createdAt.ToUniversalTime(),
            UpdatedAt = createdAt.ToUniversalTime(),
            Source = source.Trim(),
            ExternalId = externalId.Trim(),
            DeviceId = deviceId.Trim(),
            SyncVersion = 1
        };
    }

    public bool UpdateFromSync(
        double value,
        string unit,
        DateTimeOffset observedAt,
        string deviceId,
        DateTimeOffset updatedAt)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be finite and non-negative.");
        if (!AllowedUnits.TryGetValue(Metric, out var units) || !units.Contains(unit))
            throw new ArgumentException("Unit is not supported for this metric.", nameof(unit));
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Trim().Length > 128)
            throw new ArgumentException("A valid device id is required.", nameof(deviceId));

        var timestamp = observedAt.ToUniversalTime();
        var updated = updatedAt.ToUniversalTime();
        if (Value == value && Unit == unit && ObservedAt == timestamp && DeviceId == deviceId.Trim())
            return false;

        Value = value;
        Unit = unit;
        ObservedAt = timestamp;
        DeviceId = deviceId.Trim();
        UpdatedAt = updated;
        SyncVersion++;
        return true;
    }
}
