using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class NutritionGoalTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");

    [Fact]
    public void NutritionAnalyzerCalculatesMifflinStJeorTdeeAndFatLossMacros()
    {
        var analyzer = new NutritionAnalyzer();

        var result = analyzer.Calculate(
            80,
            180,
            30,
            NutritionSex.Male,
            NutritionActivityLevel.Moderate,
            GoalType.FatLoss);

        Assert.Equal(1780, result.Bmr);
        Assert.Equal(2759, result.Tdee);
        Assert.Equal(-413.9, result.AdjustmentCalories);
        Assert.Equal(2345.2, result.CalorieTarget);
        Assert.Equal(128, result.ProteinGrams);
        Assert.Equal(64, result.FatGrams);
        Assert.True(result.CarbohydrateGrams > 0);
        Assert.Equal(
            result.CalorieTarget,
            result.ProteinGrams * 4 + result.FatGrams * 9 + result.CarbohydrateGrams * 4,
            precision: 0);
    }

    [Fact]
    public void NutritionAnalyzerCalculatesFemaleBmrAndGradualMuscleGainSurplus()
    {
        var result = new NutritionAnalyzer().Calculate(
            60,
            165,
            28,
            NutritionSex.Female,
            NutritionActivityLevel.Light,
            GoalType.MuscleGain);

        Assert.Equal(1330.3, result.Bmr);
        Assert.Equal(1829.1, result.Tdee);
        Assert.Equal(250, result.AdjustmentCalories);
        Assert.Equal(2079.1, result.CalorieTarget);
        Assert.Equal(108, result.ProteinGrams);
    }

    [Fact]
    public void NutritionAnalyzerRejectsInvalidInputsAndUsesMaintenanceForGeneralHealth()
    {
        var analyzer = new NutritionAnalyzer();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            analyzer.Calculate(70, 170, 17, NutritionSex.Female, NutritionActivityLevel.Light, GoalType.GeneralHealth));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            analyzer.Calculate(0, 170, 30, NutritionSex.Female, NutritionActivityLevel.Light, GoalType.GeneralHealth));

        var result = analyzer.Calculate(
            70,
            170,
            30,
            NutritionSex.Female,
            NutritionActivityLevel.Light,
            GoalType.GeneralHealth);
        Assert.Equal(0, result.AdjustmentCalories);
        Assert.Equal(result.Tdee, result.CalorieTarget);
    }

    [Fact]
    public void GoalTypeParserSupportsProductGoalsOnly()
    {
        Assert.True(GoalTypeParser.TryParse("fat_loss", out var fatLoss));
        Assert.Equal(GoalType.FatLoss, fatLoss);
        Assert.True(GoalTypeParser.TryParse("improve_endurance", out var endurance));
        Assert.Equal(GoalType.Endurance, endurance);
        Assert.False(GoalTypeParser.TryParse("nutrition_coach", out _));
    }

    [Fact]
    public void GoalAnalyzerClampsProgressBetweenZeroAndOneHundred()
    {
        var goal = Goal.Create(
            "user-1",
            GoalType.FatLoss,
            70,
            DateOnly.FromDateTime(Now.UtcDateTime),
            null,
            Now);
        goal.Progress.Add(GoalProgress.Create(
            goal.Id,
            DateOnly.FromDateTime(Now.UtcDateTime),
            80,
            0,
            null,
            null,
            Now));
        var analyzer = new GoalAnalyzer();

        Assert.Equal(50, analyzer.CompletionPercent(goal, 75));
        Assert.Equal(100, analyzer.CompletionPercent(goal, 70));
        Assert.Equal(0, analyzer.CompletionPercent(goal, 85));
        Assert.Null(analyzer.CompletionPercent(goal, null));
    }

    [Fact]
    public void NutritionProgressAnalyzerAggregatesMealLogsAndAdherence()
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var logs = new[]
        {
            NutritionLog.Create("user-1", MealType.Breakfast, "Oats", 400, 20, 10, 60, today, Now),
            NutritionLog.Create("user-1", MealType.Lunch, "Lunch", 600, 35, 20, 70, today, Now)
        };
        var target = MacroTarget.Create("user-1", null, 2_000, 120, 60, 250, today, Now);

        var summary = new NutritionProgressAnalyzer().Analyze(today, logs, target);

        Assert.Equal(1_000, summary.Calories);
        Assert.Equal(55, summary.ProteinGrams);
        Assert.Equal(30, summary.FatGrams);
        Assert.Equal(130, summary.CarbohydrateGrams);
        Assert.Equal(50, summary.CalorieAdherencePercent);
    }

    [Fact]
    public void WeightTrendAnalyzerComparesDailyAveragesOverTwoWeeks()
    {
        IReadOnlyList<AIHealthMetricContext> metrics =
        [
            new("body_mass", 80, "kg", Now.AddDays(-12)),
            new("body_mass", 79.8, "kg", Now.AddDays(-10)),
            new("body_mass", 79, "kg", Now.AddDays(-5)),
            new("body_mass", 78.8, "kg", Now.AddDays(-2))
        ];

        var trend = new WeightTrendAnalyzer().Analyze(metrics, Now);

        Assert.Equal(-1, trend);
    }

    [Fact]
    public async Task WellnessRepositoryPersistsLogsAndRecalculatesDailySummary()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        await dbContext.SaveChangesAsync();
        var repository = new WellnessRepository(dbContext);
        var date = DateOnly.FromDateTime(Now.UtcDateTime);

        await repository.AddNutritionLogAsync(
            NutritionLog.Create("user-1", MealType.Breakfast, "Oats", 400, 20, 10, 60, date, Now),
            CancellationToken.None);
        await repository.AddNutritionLogAsync(
            NutritionLog.Create("user-1", MealType.Lunch, "Lunch", 600, 35, 20, 70, date, Now.AddHours(4)),
            CancellationToken.None);

        var logs = await repository.GetNutritionLogsAsync("user-1", date, date, CancellationToken.None);
        var summary = Assert.Single(await repository.GetNutritionSummariesAsync("user-1", date, date, CancellationToken.None));
        Assert.Equal(2, logs.Count);
        Assert.Equal(1_000, summary.Calories);
        Assert.Equal(55, summary.ProteinGrams);
        Assert.Equal(30, summary.FatGrams);
        Assert.Equal(130, summary.CarbohydrateGrams);
    }

    [Fact]
    public async Task AIContextSourceIncludesActiveGoalCalorieTargetsWeightAndNutritionAdherence()
    {
        // AIContextSource reads the last 7/14 days from the wall clock, so this data must be recent.
        var today = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", today));
        dbContext.UserProfiles.Add(UserProfile.Create("user-1", "Ada", 30, 170, "fat_loss", today));
        dbContext.NutritionProfiles.Add(NutritionProfile.Create(
            "user-1",
            NutritionSex.Female,
            NutritionActivityLevel.Moderate,
            today));
        var goal = Goal.Create(
            "user-1",
            GoalType.FatLoss,
            65,
            DateOnly.FromDateTime(today.UtcDateTime),
            null,
            today);
        dbContext.Goals.Add(goal);
        dbContext.HealthMetrics.Add(HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            "body_mass",
            70,
            "kg",
            today.AddDays(-1),
            today,
            "apple_health",
            "weight-1",
            "device-1"));
        dbContext.DailyNutritionSummaries.Add(DailyNutritionSummary.Create(
            "user-1",
            DateOnly.FromDateTime(today.UtcDateTime),
            1_000,
            80,
            40,
            100,
            today));
        await dbContext.SaveChangesAsync();

        var context = await new AIContextSource(dbContext).GetContextAsync(
            "user-1",
            today.AddDays(-30),
            CancellationToken.None);

        Assert.NotNull(context.Nutrition);
        Assert.Equal("FatLoss", context.Nutrition.ActiveGoal);
        Assert.Equal(65, context.Nutrition.TargetWeightKilograms);
        Assert.Equal(70, context.Nutrition.CurrentWeightKilograms);
        Assert.NotNull(context.Nutrition.Tdee);
        Assert.NotNull(context.Nutrition.CalorieTarget);
        Assert.NotNull(context.Nutrition.NutritionAdherencePercent);
        Assert.InRange(context.Nutrition.NutritionAdherencePercent.Value, 0, 100);
    }

    private static HealthMetric Health(string metric, double value, DateTimeOffset time) =>
        HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            metric,
            value,
            metric == "body_mass" ? "kg" : "count",
            time,
            Now,
            "apple_health",
            Guid.NewGuid().ToString(),
            "device-1");
}
