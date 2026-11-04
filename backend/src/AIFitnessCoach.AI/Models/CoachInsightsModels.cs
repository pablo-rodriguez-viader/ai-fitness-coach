namespace AIFitnessCoach.AI.Models;

public sealed class UserInsights
{
    private UserInsights() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public double? WeightChangeKilograms { get; private set; }
    public double? SleepChangeHours { get; private set; }
    public double? StepsChangePercent { get; private set; }
    public double? HeartRateChange { get; private set; }
    public double? ActiveCaloriesChange { get; private set; }
    public double? TotalCaloriesChange { get; private set; }
    public double? TrainingAdherenceRate { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; private set; }

    public static UserInsights Create(
        string userId,
        DateOnly date,
        double? weightChangeKilograms,
        double? sleepChangeHours,
        double? stepsChangePercent,
        double? heartRateChange,
        double? activeCaloriesChange,
        double? totalCaloriesChange,
        double? trainingAdherenceRate,
        string summary,
        DateTimeOffset generatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = date,
            WeightChangeKilograms = weightChangeKilograms,
            SleepChangeHours = sleepChangeHours,
            StepsChangePercent = stepsChangePercent,
            HeartRateChange = heartRateChange,
            ActiveCaloriesChange = activeCaloriesChange,
            TotalCaloriesChange = totalCaloriesChange,
            TrainingAdherenceRate = trainingAdherenceRate,
            Summary = summary,
            GeneratedAt = generatedAt
        };

    public void UpdateFrom(UserInsights source)
    {
        WeightChangeKilograms = source.WeightChangeKilograms;
        SleepChangeHours = source.SleepChangeHours;
        StepsChangePercent = source.StepsChangePercent;
        HeartRateChange = source.HeartRateChange;
        ActiveCaloriesChange = source.ActiveCaloriesChange;
        TotalCaloriesChange = source.TotalCaloriesChange;
        TrainingAdherenceRate = source.TrainingAdherenceRate;
        Summary = source.Summary;
        GeneratedAt = source.GeneratedAt;
    }
}

public sealed class BehaviorPattern
{
    private BehaviorPattern() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public double Confidence { get; private set; }
    public int EvidenceCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static BehaviorPattern Create(
        string userId,
        string key,
        string description,
        double confidence,
        int evidenceCount,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Key = key,
            Description = description,
            Confidence = Math.Clamp(confidence, 0, 1),
            EvidenceCount = Math.Max(0, evidenceCount),
            UpdatedAt = updatedAt
        };

    public void UpdateFrom(BehaviorPattern source)
    {
        Description = source.Description;
        Confidence = source.Confidence;
        EvidenceCount = source.EvidenceCount;
        UpdatedAt = source.UpdatedAt;
    }
}

public sealed class TrainingAdherence
{
    private TrainingAdherence() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public int PlannedSessions { get; private set; }
    public int CompletedSessions { get; private set; }
    public double Rate { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TrainingAdherence Create(
        string userId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int plannedSessions,
        int completedSessions,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PlannedSessions = plannedSessions,
            CompletedSessions = completedSessions,
            Rate = plannedSessions <= 0 ? 0 : Math.Clamp((double)completedSessions / plannedSessions, 0, 1),
            UpdatedAt = updatedAt
        };

    public void UpdateFrom(TrainingAdherence source)
    {
        PeriodEnd = source.PeriodEnd;
        PlannedSessions = source.PlannedSessions;
        CompletedSessions = source.CompletedSessions;
        Rate = source.Rate;
        UpdatedAt = source.UpdatedAt;
    }
}

public sealed class GoalAchievement
{
    private GoalAchievement() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid GoalId { get; private set; }
    public string Goal { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public double? ProgressPercent { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GoalAchievement Create(
        string userId,
        Guid goalId,
        string goal,
        string status,
        double? progressPercent,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GoalId = goalId,
            Goal = goal,
            Status = status,
            ProgressPercent = progressPercent is null ? null : Math.Clamp(progressPercent.Value, 0, 100),
            UpdatedAt = updatedAt
        };

    public void UpdateFrom(GoalAchievement source)
    {
        Goal = source.Goal;
        Status = source.Status;
        ProgressPercent = source.ProgressPercent;
        UpdatedAt = source.UpdatedAt;
    }
}

public sealed class RecommendationHistory
{
    private RecommendationHistory() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid? WorkoutPlanId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static RecommendationHistory Create(
        string userId,
        Guid? workoutPlanId,
        string type,
        string text,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkoutPlanId = workoutPlanId,
            Type = type,
            Text = text,
            CreatedAt = createdAt
        };
}

public sealed class RecommendationOutcome
{
    private RecommendationOutcome() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid RecommendationHistoryId { get; private set; }
    public bool? Completed { get; private set; }
    public string Feedback { get; private set; } = string.Empty;
    public double Score { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static RecommendationOutcome Create(
        string userId,
        Guid recommendationHistoryId,
        bool? completed,
        string feedback,
        double score,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RecommendationHistoryId = recommendationHistoryId,
            Completed = completed,
            Feedback = feedback,
            Score = Math.Clamp(score, 0, 1),
            CreatedAt = createdAt
        };
}
