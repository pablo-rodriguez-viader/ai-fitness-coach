using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Services;

public sealed class NutritionService(
    IWellnessRepository repository,
    MetabolicAnalyzer nutritionAnalyzer,
    NutritionProgressAnalyzer nutritionProgressAnalyzer,
    ProgressAnalyzer progressAnalyzer,
    TimeProvider timeProvider)
{
    public async Task<GoalView> CreateGoalAsync(
        string userId,
        GoalRequest request,
        CancellationToken cancellationToken)
    {
        if (!GoalTypeParser.TryParse(request.Type, out var goalType))
            throw new ArgumentException("Unsupported goal type.", nameof(request));
        var now = timeProvider.GetUtcNow();
        var start = DateOnly.FromDateTime(now.UtcDateTime);
        if (request.TargetDate is { } targetDate && targetDate < start)
            throw new ArgumentException("Target date cannot be in the past.", nameof(request));
        var goal = Goal.Create(userId, goalType, request.TargetWeightKilograms, start, request.TargetDate, now);
        await repository.PauseActiveGoalsAsync(userId, now, cancellationToken);
        await repository.AddGoalAsync(goal, cancellationToken);
        var startingWeight = (await repository.GetWeightMetricsAsync(
                userId,
                now.AddYears(-2),
                cancellationToken))
            .OrderByDescending(metric => metric.ObservedAt)
            .Select(metric => (double?)metric.Value)
            .FirstOrDefault();
        if (startingWeight is { } weight)
        {
            await repository.SaveGoalProgressAsync(
                GoalProgress.Create(goal.Id, start, weight, 0, null, null, now),
                cancellationToken);
        }
        await RecalculateTargetsAsync(userId, goal, now, cancellationToken);
        return ToView(goal);
    }

    public async Task<IReadOnlyList<GoalView>> GetGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        var goals = await repository.GetGoalsAsync(userId, cancellationToken);
        return goals.Select(ToView).ToArray();
    }

    public async Task<NutritionProfileView> SetNutritionProfileAsync(
        string userId,
        NutritionProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<NutritionSex>(request.Sex, true, out var sex)
            || !Enum.IsDefined(sex)
            || !Enum.TryParse<NutritionActivityLevel>(request.ActivityLevel, true, out var activity)
            || !Enum.IsDefined(activity))
            throw new ArgumentException("Valid sex (Male/Female) and activity level are required.", nameof(request));
        var now = timeProvider.GetUtcNow();
        var existing = await repository.GetNutritionProfileAsync(userId, cancellationToken);
        var profile = existing ?? NutritionProfile.Create(userId, sex, activity, now);
        if (existing is not null)
            profile.Update(sex, activity, now);
        var goal = await repository.GetActiveGoalAsync(userId, cancellationToken);
        var calculation = await CalculateAsync(userId, profile, goal, cancellationToken);
        await repository.SaveNutritionProfileAsync(profile, cancellationToken);
        await SaveMacroTargetAsync(userId, goal, calculation, now, cancellationToken);
        return ToNutritionView(calculation);
    }

    public async Task<NutritionProfileView> GetNutritionProfileAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var profile = await repository.GetNutritionProfileAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Set a nutrition profile before requesting calorie targets.");
        var goal = await repository.GetActiveGoalAsync(userId, cancellationToken);
        return ToNutritionView(await CalculateAsync(userId, profile, goal, cancellationToken));
    }

    public async Task<DailyNutritionSummaryView> AddNutritionLogAsync(
        string userId,
        NutritionLogRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MealType>(request.MealType, true, out var mealType) || !Enum.IsDefined(mealType))
            throw new ArgumentException("Meal type must be Breakfast, Lunch, Dinner, or Snack.", nameof(request));
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var date = request.Date ?? today;
        if (date > today || date < today.AddDays(-365))
            throw new ArgumentException("Nutrition log date must be within the past 365 days.", nameof(request));
        var now = timeProvider.GetUtcNow();
        await repository.AddNutritionLogAsync(
            NutritionLog.Create(
                userId,
                mealType,
                request.Name,
                request.Calories,
                request.ProteinGrams,
                request.FatGrams,
                request.CarbohydrateGrams,
                date,
                now),
            cancellationToken);
        return await GetNutritionSummaryAsync(userId, date, cancellationToken);
    }

    public async Task<DailyNutritionSummaryView> GetNutritionSummaryAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var logs = await repository.GetNutritionLogsAsync(userId, date, date, cancellationToken);
        var target = await repository.GetMacroTargetAsync(userId, date, cancellationToken);
        return nutritionProgressAnalyzer.Analyze(date, logs, target);
    }

    public async Task<ProgressView> GetProgressAsync(string userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var goal = await repository.GetActiveGoalAsync(userId, cancellationToken);
        var from = now.AddDays(-90);
        var healthMetrics = await repository.GetProgressMetricsAsync(userId, from, cancellationToken);
        var nutritionSummaries = await repository.GetNutritionSummariesAsync(
            userId,
            DateOnly.FromDateTime(from.UtcDateTime),
            DateOnly.FromDateTime(now.UtcDateTime),
            cancellationToken);
        var savedProgress = goal is null
            ? Array.Empty<GoalProgress>()
            : (await repository.GetGoalProgressAsync(goal.Id, cancellationToken)).ToArray();
        return progressAnalyzer.Analyze(goal, healthMetrics, nutritionSummaries, savedProgress);
    }

    private async Task<NutritionCalculation> CalculateAsync(
        string userId,
        NutritionProfile profile,
        Goal? goal,
        CancellationToken cancellationToken)
    {
        var userProfile = await repository.GetUserProfileAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Create a user profile before calculating nutrition targets.");
        var weight = (await repository.GetWeightMetricsAsync(
                userId,
                timeProvider.GetUtcNow().AddYears(-2),
                cancellationToken))
            .OrderByDescending(metric => metric.ObservedAt)
            .Select(metric => (double?)metric.Value)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("A current HealthKit weight measurement is required.");
        return nutritionAnalyzer.Calculate(
            weight,
            userProfile.HeightCentimeters,
            userProfile.Age,
            profile.Sex,
            profile.ActivityLevel,
            goal?.Type ?? GoalType.GeneralHealth);
    }

    private async Task RecalculateTargetsAsync(
        string userId,
        Goal goal,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profile = await repository.GetNutritionProfileAsync(userId, cancellationToken);
        var userProfile = await repository.GetUserProfileAsync(userId, cancellationToken);
        if (profile is null || userProfile is null || userProfile.Age < 18)
            return;
        var recentWeights = await repository.GetWeightMetricsAsync(
            userId,
            now.AddYears(-2),
            cancellationToken);
        if (recentWeights.Count == 0
            || recentWeights[0].Value is < 20 or > 500
            || userProfile.HeightCentimeters is < 100 or > 300)
            return;
        var calculation = await CalculateAsync(userId, profile, goal, cancellationToken);
        await SaveMacroTargetAsync(userId, goal, calculation, now, cancellationToken);
    }

    private async Task SaveMacroTargetAsync(
        string userId,
        Goal? goal,
        NutritionCalculation calculation,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await repository.SaveMacroTargetAsync(
            MacroTarget.Create(
                userId,
                goal?.Id,
                calculation.CalorieTarget,
                calculation.ProteinGrams,
                calculation.FatGrams,
                calculation.CarbohydrateGrams,
                DateOnly.FromDateTime(now.UtcDateTime),
                now),
            cancellationToken);

    private static NutritionProfileView ToNutritionView(NutritionCalculation calculation) =>
        new(
            calculation.WeightKilograms,
            calculation.Bmr,
            calculation.Tdee,
            calculation.CalorieTarget,
            calculation.AdjustmentCalories,
            calculation.ProteinGrams,
            calculation.FatGrams,
            calculation.CarbohydrateGrams,
            calculation.Goal.ToString(),
            calculation.SafetyWarnings);

    private static GoalView ToView(Goal goal) =>
        new(
            goal.Id,
            goal.Type.ToString(),
            goal.Status.ToString(),
            goal.TargetWeightKilograms,
            goal.StartsOn,
            goal.TargetDate,
            goal.CreatedAt);
}
