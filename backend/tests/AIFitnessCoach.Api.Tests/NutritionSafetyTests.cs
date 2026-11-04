using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class NutritionSafetyTests
{
    [Fact]
    public void MetabolicAnalyzerUsesMifflinStJeorAndActivityFactor()
    {
        var result = new MetabolicAnalyzer().Calculate(
            weightKilograms: 80,
            heightCentimeters: 180,
            age: 35,
            sex: NutritionSex.Male,
            activityLevel: NutritionActivityLevel.Moderate,
            goal: GoalType.WeightMaintenance);

        Assert.Equal(1_755, result.Bmr);
        Assert.Equal(2_720.3, result.Tdee);
    }

    [Fact]
    public void NutritionSafetyAnalyzerFlagsExcessiveDeficitAndSurplus()
    {
        var analyzer = new NutritionSafetyAnalyzer();

        Assert.Contains("déficit", Assert.Single(analyzer.AnalyzeCalorieAdjustment(2_000, -600)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("superávit", Assert.Single(analyzer.AnalyzeCalorieAdjustment(2_000, 600)), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(analyzer.AnalyzeCalorieAdjustment(2_000, -300));
        Assert.Empty(analyzer.AnalyzeCalorieAdjustment(2_000, 300));
    }

    [Fact]
    public void WeightTrendAnalyzerFindsWeeklyRateAndRapidLossWarning()
    {
        var now = DateTimeOffset.UtcNow;
        var metrics = new[]
        {
            new AIHealthMetricContext("body_mass", 80, "kg", now.AddDays(-10)),
            new AIHealthMetricContext("body_mass", 78, "kg", now.AddDays(-3))
        };
        var analyzer = new WeightTrendAnalyzer();

        Assert.Equal(-2, analyzer.AnalyzeWeeklyRate(metrics, now));
        var progress = new ProgressAnalyzer().Analyze(null, metrics, [], []);
        Assert.Contains(progress.SafetyWarnings!, warning => warning.Contains("pérdida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgressAnalyzerFlagsRapidWeightGain()
    {
        var now = DateTimeOffset.UtcNow;
        var metrics = new[]
        {
            new AIHealthMetricContext("body_mass", 70, "kg", now.AddDays(-10)),
            new AIHealthMetricContext("body_mass", 71, "kg", now.AddDays(-3))
        };

        var progress = new ProgressAnalyzer().Analyze(null, metrics, [], []);

        Assert.Contains(progress.SafetyWarnings!, warning => warning.Contains("ganancia", StringComparison.OrdinalIgnoreCase));
    }
}
