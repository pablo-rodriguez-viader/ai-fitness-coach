using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Contracts;

public sealed record GoalRequest(string Type, double? TargetWeightKilograms, DateOnly? TargetDate);
public sealed record GoalView(
    Guid Id,
    string Type,
    string Status,
    double? TargetWeightKilograms,
    DateOnly StartsOn,
    DateOnly? TargetDate,
    DateTimeOffset CreatedAt);

public sealed record NutritionProfileRequest(string Sex, string ActivityLevel);
public sealed record NutritionLogRequest(
    string MealType,
    string Name,
    double Calories,
    double ProteinGrams,
    double FatGrams,
    double CarbohydrateGrams,
    DateOnly? Date);

public sealed record NutritionProfileView(
    double WeightKilograms,
    double Bmr,
    double Tdee,
    double DailyCalorieTarget,
    double DeficitOrSurplusCalories,
    double ProteinGrams,
    double FatGrams,
    double CarbohydrateGrams,
    string Goal,
    IReadOnlyList<string>? SafetyWarnings = null);

public sealed record DailyNutritionSummaryView(
    DateOnly Date,
    double Calories,
    double ProteinGrams,
    double FatGrams,
    double CarbohydrateGrams,
    double? CalorieTarget,
    double? CalorieAdherencePercent);

public sealed record ProgressPoint(
    DateOnly Date,
    double? WeightKilograms,
    double? Calories,
    double? Steps,
    double? SleepHours);

public sealed record ProgressView(
    GoalView? ActiveGoal,
    double? CurrentWeightKilograms,
    double? TargetWeightKilograms,
    double? WeightChangeKilograms,
    double? GoalCompletionPercent,
    IReadOnlyList<ProgressPoint> History,
    double? WeightChangePerWeekKilograms = null,
    IReadOnlyList<string>? SafetyWarnings = null);

public sealed record NutritionCalculation(
    double WeightKilograms,
    double Bmr,
    double Tdee,
    double CalorieTarget,
    double AdjustmentCalories,
    double ProteinGrams,
    double FatGrams,
    double CarbohydrateGrams,
    GoalType Goal,
    IReadOnlyList<string>? SafetyWarnings = null);

public sealed record AIUserNutritionContext(
    string? ActiveGoal,
    double? TargetWeightKilograms,
    double? Bmr,
    double? Tdee,
    double? CalorieTarget,
    double? CurrentWeightKilograms,
    double? WeightTrendKilograms,
    double? NutritionAdherencePercent);

public sealed record NutritionUserProfile(int Age, double HeightCentimeters);

public static class GoalTypeParser
{
    public static bool TryParse(string? value, out GoalType goal)
    {
        goal = value?.Trim().ToLowerInvariant() switch
        {
            "fat_loss" or "lose_fat" => GoalType.FatLoss,
            "muscle_gain" or "gain_muscle" => GoalType.MuscleGain,
            "weight_maintenance" or "maintain_weight" => GoalType.WeightMaintenance,
            "endurance" or "improve_endurance" => GoalType.Endurance,
            "general_health" or "improve_general_health" => GoalType.GeneralHealth,
            _ => (GoalType)(-1)
        };
        return Enum.IsDefined(goal);
    }
}
