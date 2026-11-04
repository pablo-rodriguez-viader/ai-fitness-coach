namespace AIFitnessCoach.AI.Models;

public enum GoalType
{
    FatLoss,
    MuscleGain,
    WeightMaintenance,
    Endurance,
    GeneralHealth
}

public enum GoalStatus
{
    Active,
    Completed,
    Paused,
    Abandoned
}

public enum NutritionSex
{
    Male,
    Female
}

public enum NutritionActivityLevel
{
    Sedentary,
    Light,
    Moderate,
    High,
    Athlete
}

public enum MealType
{
    Breakfast,
    Lunch,
    Dinner,
    Snack
}

public sealed class Goal
{
    private Goal() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public GoalType Type { get; private set; }
    public GoalStatus Status { get; private set; }
    public double? TargetWeightKilograms { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<GoalProgress> Progress { get; private set; } = new List<GoalProgress>();

    public static Goal Create(
        string userId,
        GoalType type,
        double? targetWeightKilograms,
        DateOnly startsOn,
        DateOnly? targetDate,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
        if (targetWeightKilograms is { } target && (!double.IsFinite(target) || target is < 20 or > 500))
            throw new ArgumentOutOfRangeException(nameof(targetWeightKilograms));
        if (targetDate is { } end && end < startsOn)
            throw new ArgumentException("Target date must not precede the start date.", nameof(targetDate));
        return new Goal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Status = GoalStatus.Active,
            TargetWeightKilograms = targetWeightKilograms,
            StartsOn = startsOn,
            TargetDate = targetDate,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime()
        };
    }

    public void SetStatus(GoalStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now.ToUniversalTime();
    }
}

public sealed class GoalProgress
{
    private GoalProgress() { }

    public Guid Id { get; private set; }
    public Guid GoalId { get; private set; }
    public DateOnly Date { get; private set; }
    public double? WeightKilograms { get; private set; }
    public double? CompletionPercent { get; private set; }
    public double? Steps { get; private set; }
    public double? SleepHours { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static GoalProgress Create(
        Guid goalId,
        DateOnly date,
        double? weightKilograms,
        double? completionPercent,
        double? steps,
        double? sleepHours,
        DateTimeOffset now)
    {
        if (goalId == Guid.Empty)
            throw new ArgumentException("A goal is required.", nameof(goalId));
        ValidateNonNegative(weightKilograms, nameof(weightKilograms));
        if (completionPercent is { } completion && (!double.IsFinite(completion) || completion is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(completionPercent));
        ValidateNonNegative(steps, nameof(steps));
        ValidateNonNegative(sleepHours, nameof(sleepHours));
        return new GoalProgress
        {
            Id = Guid.NewGuid(),
            GoalId = goalId,
            Date = date,
            WeightKilograms = weightKilograms,
            CompletionPercent = completionPercent,
            Steps = steps,
            SleepHours = sleepHours,
            CreatedAt = now.ToUniversalTime()
        };
    }

    public void Update(
        double? weightKilograms,
        double? completionPercent,
        double? steps,
        double? sleepHours,
        DateTimeOffset now)
    {
        ValidateNonNegative(weightKilograms, nameof(weightKilograms));
        if (completionPercent is { } completion && (!double.IsFinite(completion) || completion is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(completionPercent));
        ValidateNonNegative(steps, nameof(steps));
        ValidateNonNegative(sleepHours, nameof(sleepHours));
        WeightKilograms = weightKilograms;
        CompletionPercent = completionPercent;
        Steps = steps;
        SleepHours = sleepHours;
        CreatedAt = now.ToUniversalTime();
    }

    private static void ValidateNonNegative(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class NutritionProfile
{
    private NutritionProfile() { }

    public string UserId { get; private set; } = string.Empty;
    public NutritionSex Sex { get; private set; }
    public NutritionActivityLevel ActivityLevel { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static NutritionProfile Create(
        string userId,
        NutritionSex sex,
        NutritionActivityLevel activityLevel,
        DateTimeOffset now) =>
        new()
        {
            UserId = userId,
            Sex = sex,
            ActivityLevel = activityLevel,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime()
        };

    public void Update(NutritionSex sex, NutritionActivityLevel activityLevel, DateTimeOffset now)
    {
        Sex = sex;
        ActivityLevel = activityLevel;
        UpdatedAt = now.ToUniversalTime();
    }
}

public sealed class NutritionLog
{
    private NutritionLog() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public MealType MealType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public double Calories { get; private set; }
    public double ProteinGrams { get; private set; }
    public double FatGrams { get; private set; }
    public double CarbohydrateGrams { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static NutritionLog Create(
        string userId,
        MealType mealType,
        string name,
        double calories,
        double proteinGrams,
        double fatGrams,
        double carbohydrateGrams,
        DateOnly date,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new ArgumentException("Meal name must contain 1 to 160 characters.", nameof(name));
        ValidateAmount(calories, nameof(calories), 10_000);
        ValidateAmount(proteinGrams, nameof(proteinGrams), 1_000);
        ValidateAmount(fatGrams, nameof(fatGrams), 1_000);
        ValidateAmount(carbohydrateGrams, nameof(carbohydrateGrams), 2_000);
        return new NutritionLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MealType = mealType,
            Name = name.Trim(),
            Calories = calories,
            ProteinGrams = proteinGrams,
            FatGrams = fatGrams,
            CarbohydrateGrams = carbohydrateGrams,
            Date = date,
            CreatedAt = now.ToUniversalTime()
        };
    }

    private static void ValidateAmount(double amount, string name, double maximum)
    {
        if (!double.IsFinite(amount) || amount < 0 || amount > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class MacroTarget
{
    private MacroTarget() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid? GoalId { get; private set; }
    public double Calories { get; private set; }
    public double ProteinGrams { get; private set; }
    public double FatGrams { get; private set; }
    public double CarbohydrateGrams { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MacroTarget Create(
        string userId,
        Guid? goalId,
        double calories,
        double proteinGrams,
        double fatGrams,
        double carbohydrateGrams,
        DateOnly effectiveFrom,
        DateTimeOffset now)
    {
        foreach (var (value, name) in new[]
                 {
                     (calories, nameof(calories)),
                     (proteinGrams, nameof(proteinGrams)),
                     (fatGrams, nameof(fatGrams)),
                     (carbohydrateGrams, nameof(carbohydrateGrams))
                 })
        {
            if (!double.IsFinite(value) || value < 0 || value > 10_000)
                throw new ArgumentOutOfRangeException(name);
        }
        return new MacroTarget
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GoalId = goalId,
            Calories = calories,
            ProteinGrams = proteinGrams,
            FatGrams = fatGrams,
            CarbohydrateGrams = carbohydrateGrams,
            EffectiveFrom = effectiveFrom,
            CreatedAt = now.ToUniversalTime()
        };
    }
}

public sealed class DailyNutritionSummary
{
    private DailyNutritionSummary() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public double Calories { get; private set; }
    public double ProteinGrams { get; private set; }
    public double FatGrams { get; private set; }
    public double CarbohydrateGrams { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Recalculate(
        double calories,
        double proteinGrams,
        double fatGrams,
        double carbohydrateGrams,
        DateTimeOffset updatedAt)
    {
        Calories = calories;
        ProteinGrams = proteinGrams;
        FatGrams = fatGrams;
        CarbohydrateGrams = carbohydrateGrams;
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public static DailyNutritionSummary Create(
        string userId,
        DateOnly date,
        double calories,
        double proteinGrams,
        double fatGrams,
        double carbohydrateGrams,
        DateTimeOffset now)
    {
        var summary = new DailyNutritionSummary
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = date
        };
        summary.Recalculate(calories, proteinGrams, fatGrams, carbohydrateGrams, now);
        return summary;
    }
}
