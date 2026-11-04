using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class NutritionAnalyzer
{
    public NutritionCalculation Calculate(
        double weightKilograms,
        double heightCentimeters,
        int age,
        NutritionSex sex,
        NutritionActivityLevel activityLevel,
        GoalType goal)
    {
        if (!double.IsFinite(weightKilograms) || weightKilograms is < 20 or > 500)
            throw new ArgumentOutOfRangeException(nameof(weightKilograms));
        if (!double.IsFinite(heightCentimeters) || heightCentimeters is <= 0 or > 300)
            throw new ArgumentOutOfRangeException(nameof(heightCentimeters));
        if (age is < 18 or > 120)
            throw new ArgumentOutOfRangeException(nameof(age), "Nutrition calorie calculations require age 18 or above.");

        var sexAdjustment = sex switch
        {
            NutritionSex.Male => 5,
            NutritionSex.Female => -161,
            _ => throw new ArgumentOutOfRangeException(nameof(sex))
        };
        var activityMultiplier = activityLevel switch
        {
            NutritionActivityLevel.Sedentary => 1.2,
            NutritionActivityLevel.Light => 1.375,
            NutritionActivityLevel.Moderate => 1.55,
            NutritionActivityLevel.High => 1.725,
            NutritionActivityLevel.Athlete => 1.9,
            _ => throw new ArgumentOutOfRangeException(nameof(activityLevel))
        };
        var bmr = 10 * weightKilograms + 6.25 * heightCentimeters - 5 * age + sexAdjustment;
        if (!double.IsFinite(bmr) || bmr <= 0)
            throw new ArgumentOutOfRangeException(nameof(weightKilograms), "Profile values do not produce a valid BMR.");
        var tdee = bmr * activityMultiplier;
        var adjustment = goal switch
        {
            GoalType.FatLoss => -Math.Min(500, tdee * 0.15),
            GoalType.MuscleGain => 250,
            GoalType.Endurance => tdee * 0.05,
            GoalType.WeightMaintenance or GoalType.GeneralHealth => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(goal))
        };
        var calorieTarget = tdee + adjustment;
        var protein = weightKilograms * (goal == GoalType.MuscleGain ? 1.8 : 1.6);
        var fat = weightKilograms * 0.8;
        var carbohydrates = Math.Max(0, (calorieTarget - protein * 4 - fat * 9) / 4);
        var safetyWarnings = new NutritionSafetyAnalyzer().AnalyzeCalorieAdjustment(tdee, adjustment);
        return new NutritionCalculation(
            Round(weightKilograms),
            Round(bmr),
            Round(tdee),
            Round(calorieTarget),
            Round(adjustment),
            Round(protein),
            Round(fat),
            Round(carbohydrates),
            goal,
            safetyWarnings);
    }

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}

public sealed class GoalAnalyzer
{
    public double? CompletionPercent(Goal goal, double? currentWeightKilograms)
    {
        if (goal.TargetWeightKilograms is not { } target || currentWeightKilograms is not { } current)
            return null;
        var startingWeight = goal.Progress.OrderBy(progress => progress.Date).FirstOrDefault()?.WeightKilograms
            ?? current;
        var initialDistance = Math.Abs(startingWeight - target);
        if (initialDistance <= 0.01)
            return 100;
        var remaining = Math.Abs(current - target);
        return Math.Clamp((initialDistance - remaining) / initialDistance * 100, 0, 100);
    }
}

public sealed class NutritionProgressAnalyzer
{
    public DailyNutritionSummaryView Analyze(
        DateOnly date,
        IReadOnlyList<NutritionLog> logs,
        MacroTarget? target)
    {
        var calories = logs.Sum(log => log.Calories);
        var protein = logs.Sum(log => log.ProteinGrams);
        var fat = logs.Sum(log => log.FatGrams);
        var carbs = logs.Sum(log => log.CarbohydrateGrams);
        double? adherence = target is null || target.Calories <= 0
            ? null
            : Math.Round(calories / target.Calories * 100, 1, MidpointRounding.AwayFromZero);
        return new DailyNutritionSummaryView(
            date,
            Round(calories),
            Round(protein),
            Round(fat),
            Round(carbs),
            target?.Calories,
            adherence);
    }

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}

public sealed class WeightTrendAnalyzer
{
    public double? Analyze(IReadOnlyList<AIHealthMetricContext> metrics, DateTimeOffset now)
    {
        var recent = AverageByDay(metrics, now.AddDays(-7), now);
        var previous = AverageByDay(metrics, now.AddDays(-14), now.AddDays(-7));
        return recent is null || previous is null
            ? null
            : Math.Round(recent.Value - previous.Value, 2, MidpointRounding.AwayFromZero);
    }

    public double? AnalyzeWeeklyRate(IReadOnlyList<AIHealthMetricContext> metrics, DateTimeOffset now)
    {
        var recent = AverageByDay(metrics, now.AddDays(-7), now);
        var previous = AverageByDay(metrics, now.AddDays(-14), now.AddDays(-7));
        return recent is null || previous is null
            ? null
            : Math.Round(recent.Value - previous.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static double? AverageByDay(
        IReadOnlyList<AIHealthMetricContext> metrics,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var values = metrics.Where(metric =>
                metric.Metric == "body_mass" && metric.ObservedAt >= from && metric.ObservedAt < to)
            .GroupBy(metric => metric.ObservedAt.UtcDateTime.Date)
            .Select(day => day.OrderByDescending(metric => metric.ObservedAt).First().Value)
            .ToArray();
        return values.Length == 0 ? null : values.Average();
    }
}

public sealed class MetabolicAnalyzer
{
    private readonly NutritionAnalyzer nutritionAnalyzer = new();

    public NutritionCalculation Calculate(
        double weightKilograms,
        double heightCentimeters,
        int age,
        NutritionSex sex,
        NutritionActivityLevel activityLevel,
        GoalType goal) =>
        nutritionAnalyzer.Calculate(weightKilograms, heightCentimeters, age, sex, activityLevel, goal);
}

public sealed class NutritionSafetyAnalyzer
{
    public IReadOnlyList<string> AnalyzeCalorieAdjustment(double tdee, double adjustmentCalories)
    {
        if (!double.IsFinite(tdee) || tdee <= 0 || !double.IsFinite(adjustmentCalories))
            throw new ArgumentOutOfRangeException(nameof(tdee));
        var warnings = new List<string>();
        var safeMaximum = Math.Min(500, tdee * 0.20);
        if (adjustmentCalories < -safeMaximum)
            warnings.Add("El déficit calculado es elevado; revisa el objetivo con un profesional cualificado.");
        if (adjustmentCalories > safeMaximum)
            warnings.Add("El superávit calculado es elevado; revisa el objetivo con un profesional cualificado.");
        return warnings;
    }
}
