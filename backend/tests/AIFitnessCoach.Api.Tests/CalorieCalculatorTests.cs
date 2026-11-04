using AIFitnessCoach.Application;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class CalorieCalculatorTests
{
    [Fact]
    public void Calculate_UsesMifflinStJeorForMaleAndFemale()
    {
        var male = CalorieCalculator.Calculate(
            80, 180, 30, EquationSex.Male, ActivityLevel.Sedentary, CalorieGoal.Maintain);
        var female = CalorieCalculator.Calculate(
            80, 180, 30, EquationSex.Female, ActivityLevel.Sedentary, CalorieGoal.Maintain);

        Assert.Equal(1_780, male.BasalMetabolicRate);
        Assert.Equal(1_614, female.BasalMetabolicRate);
        Assert.Equal(2_136, male.TotalDailyEnergyExpenditure);
        Assert.Equal(1_937, female.TotalDailyEnergyExpenditure);
    }

    [Fact]
    public void Calculate_AppliesFatLossAndMuscleGainTargets()
    {
        var loss = CalorieCalculator.Calculate(
            80, 180, 30, EquationSex.Male, ActivityLevel.Moderate, CalorieGoal.FatLoss);
        var gain = CalorieCalculator.Calculate(
            80, 180, 30, EquationSex.Male, ActivityLevel.Moderate, CalorieGoal.MuscleGain);

        Assert.Equal(2_259, loss.DailyTarget);
        Assert.Equal(3_059, gain.DailyTarget);
    }

    [Fact]
    public void Calculate_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalorieCalculator.Calculate(
                0, 180, 30, EquationSex.Male, ActivityLevel.Moderate, CalorieGoal.Maintain));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalorieCalculator.Calculate(
                80, 180, 17, EquationSex.Female, ActivityLevel.Light, CalorieGoal.FatLoss));
    }

    [Fact]
    public void Calculate_RejectsNonFiniteMeasurementsAndUnknownEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalorieCalculator.Calculate(
                double.NaN, 180, 30, EquationSex.Male, ActivityLevel.Light, CalorieGoal.Maintain));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalorieCalculator.Calculate(
                80, 180, 30, (EquationSex)99, ActivityLevel.Light, CalorieGoal.Maintain));
    }
}
