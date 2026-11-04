namespace AIFitnessCoach.Domain.Entities;

public sealed class UserProfile
{
    private UserProfile()
    {
        UserId = string.Empty;
        DisplayName = string.Empty;
        Goal = string.Empty;
    }

    public string UserId { get; private set; }
    public string DisplayName { get; private set; }
    public int Age { get; private set; }
    public double HeightCentimeters { get; private set; }
    public string Goal { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserProfile Create(
        string userId,
        string displayName,
        int age,
        double heightCentimeters,
        string goal,
        DateTimeOffset now)
    {
        Validate(userId, displayName, age, heightCentimeters, goal);
        return new UserProfile
        {
            UserId = userId.Trim(),
            DisplayName = displayName.Trim(),
            Age = age,
            HeightCentimeters = heightCentimeters,
            Goal = goal.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string displayName,
        int age,
        double heightCentimeters,
        string goal,
        DateTimeOffset now)
    {
        Validate(UserId, displayName, age, heightCentimeters, goal);
        DisplayName = displayName.Trim();
        Age = age;
        HeightCentimeters = heightCentimeters;
        Goal = goal.Trim();
        UpdatedAt = now;
    }

    private static void Validate(
        string userId,
        string displayName,
        int age,
        double heightCentimeters,
        string goal)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 128)
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120)
            throw new ArgumentException("Display name must contain 1 to 120 characters.", nameof(displayName));
        if (age is < 13 or > 120)
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be between 13 and 120.");
        if (!double.IsFinite(heightCentimeters) || heightCentimeters is <= 0 or > 300)
            throw new ArgumentOutOfRangeException(
                nameof(heightCentimeters),
                "Height must be greater than 0 and at most 300 centimeters.");
        if (string.IsNullOrWhiteSpace(goal) || goal.Trim().Length > 64)
            throw new ArgumentException("Goal must contain 1 to 64 characters.", nameof(goal));
    }
}
