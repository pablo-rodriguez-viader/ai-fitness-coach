namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record GoalRequest(string? Type, double? TargetWeightKilograms, DateOnly? TargetDate);

public sealed record NutritionProfileRequest(string? Sex, string? ActivityLevel);

public sealed record NutritionLogRequest(
    string? MealType,
    string? Name,
    double Calories,
    double ProteinGrams,
    double FatGrams,
    double CarbohydrateGrams,
    DateOnly? Date);
