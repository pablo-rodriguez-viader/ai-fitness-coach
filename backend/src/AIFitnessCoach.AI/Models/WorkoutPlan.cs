namespace AIFitnessCoach.AI.Models;

public sealed class WorkoutPlan
{
    private WorkoutPlan() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string Goal { get; private set; } = string.Empty;
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ICollection<WorkoutDay> Days { get; private set; } = new List<WorkoutDay>();

    public static WorkoutPlan Create(
        string userId,
        string goal,
        DateOnly startsOn,
        DateOnly endsOn,
        IEnumerable<WorkoutDay> days,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(goal) || goal.Length > 64)
            throw new ArgumentException("A supported goal is required.", nameof(goal));
        if (endsOn < startsOn)
            throw new ArgumentException("Plan end date must not precede its start date.", nameof(endsOn));
        var planDays = days.ToArray();
        if (planDays.Length is < 1 or > 7)
            throw new ArgumentException("A workout plan must contain 1 to 7 days.", nameof(days));

        return new WorkoutPlan
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Goal = goal,
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedAt = now.ToUniversalTime(),
            Days = planDays
        };
    }
}

public sealed class WorkoutDay
{
    private WorkoutDay() { }

    public Guid Id { get; private set; }
    public Guid WorkoutPlanId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string RecoveryNote { get; private set; } = string.Empty;
    public ICollection<WorkoutExercise> Exercises { get; private set; } = new List<WorkoutExercise>();

    public static WorkoutDay Create(
        DateOnly date,
        string title,
        string recoveryNote,
        IEnumerable<WorkoutExercise> exercises)
    {
        var exerciseList = exercises.ToArray();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160)
            throw new ArgumentException("A day title is required.", nameof(title));
        if (exerciseList.Length > 20)
            throw new ArgumentException("A workout day cannot contain more than 20 exercises.", nameof(exercises));
        return new WorkoutDay
        {
            Id = Guid.NewGuid(),
            Date = date,
            Title = title.Trim(),
            RecoveryNote = Clip(recoveryNote, 1000),
            Exercises = exerciseList
        };
    }

    private static string Clip(string value, int maxLength) =>
        value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}

public sealed class WorkoutExercise
{
    private WorkoutExercise() { }

    public Guid Id { get; private set; }
    public Guid WorkoutDayId { get; private set; }
    public int Position { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Sets { get; private set; }
    public string? Repetitions { get; private set; }
    public int? DurationMinutes { get; private set; }
    public int? RestSeconds { get; private set; }
    public string? Instructions { get; private set; }

    public static WorkoutExercise Create(
        int position,
        string name,
        string? sets,
        string? repetitions,
        int? durationMinutes,
        int? restSeconds,
        string? instructions)
    {
        if (position < 0 || string.IsNullOrWhiteSpace(name) || name.Length > 160)
            throw new ArgumentException("Exercise position and name are invalid.", nameof(name));
        if (durationMinutes is < 1 or > 240 || restSeconds is < 0 or > 600)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        return new WorkoutExercise
        {
            Id = Guid.NewGuid(),
            Position = position,
            Name = name.Trim(),
            Sets = Clip(sets, 32),
            Repetitions = Clip(repetitions, 32),
            DurationMinutes = durationMinutes,
            RestSeconds = restSeconds,
            Instructions = Clip(instructions, 500)
        };
    }

    private static string? Clip(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}

public enum WorkoutDifficulty
{
    VeryEasy,
    Easy,
    Appropriate,
    Hard,
    VeryHard
}

public sealed record WorkoutSession(Guid WorkoutPlanId, WorkoutDay Day)
{
    public static WorkoutSession? FromPlan(WorkoutPlan plan, Guid workoutDayId)
    {
        var day = plan.Days.SingleOrDefault(item => item.Id == workoutDayId);
        return day is null ? null : new WorkoutSession(plan.Id, day);
    }
}

public sealed class WorkoutCompletion
{
    private WorkoutCompletion() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid WorkoutDayId { get; private set; }
    public bool Completed { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static WorkoutCompletion Create(string userId, Guid workoutDayId, bool completed, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkoutDayId = workoutDayId,
            Completed = completed,
            RecordedAt = now.ToUniversalTime()
        };
}

public sealed class WorkoutFeedback
{
    private WorkoutFeedback() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid WorkoutDayId { get; private set; }
    public bool Completed { get; private set; }
    public WorkoutDifficulty Difficulty { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkoutFeedback Create(
        string userId,
        Guid workoutDayId,
        bool completed,
        WorkoutDifficulty difficulty,
        string? comment,
        DateTimeOffset now)
    {
        if (comment?.Length > 500)
            throw new ArgumentException("Feedback comment cannot exceed 500 characters.", nameof(comment));
        return new WorkoutFeedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkoutDayId = workoutDayId,
            Completed = completed,
            Difficulty = difficulty,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            CreatedAt = now.ToUniversalTime()
        };
    }
}

public sealed class WorkoutRecommendation
{
    private WorkoutRecommendation() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid? WorkoutPlanId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkoutRecommendation Create(
        string userId,
        Guid? workoutPlanId,
        string type,
        string text,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkoutPlanId = workoutPlanId,
            Type = type,
            Text = text,
            CreatedAt = now.ToUniversalTime()
        };
}

public sealed record WorkoutHistory(
        Guid Id,
        Guid WorkoutPlanId,
        Guid WorkoutDayId,
        string Goal,
        DateOnly Date,
        string Title,
        bool Completed,
        string? Difficulty,
        DateTimeOffset RecordedAt);
