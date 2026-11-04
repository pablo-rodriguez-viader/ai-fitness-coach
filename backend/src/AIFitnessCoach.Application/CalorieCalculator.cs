namespace AIFitnessCoach.Application;

public enum EquationSex
{
    Male,
    Female
}

public enum ActivityLevel
{
    Sedentary,
    Light,
    Moderate,
    High,
    Athlete
}

public enum CalorieGoal
{
    Maintain,
    FatLoss,
    MuscleGain
}

public sealed record CalorieEstimate(
    double BasalMetabolicRate,
    double TotalDailyEnergyExpenditure,
    double DailyTarget);

public static class CalorieCalculator
{
    public static CalorieEstimate Calculate(
        double weightKilograms,
        double heightCentimeters,
        int ageYears,
        EquationSex sex,
        ActivityLevel activityLevel,
        CalorieGoal goal)
    {
        if (!double.IsFinite(weightKilograms) || weightKilograms is <= 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(weightKilograms));
        if (!double.IsFinite(heightCentimeters) || heightCentimeters is <= 0 or > 300)
            throw new ArgumentOutOfRangeException(nameof(heightCentimeters));
        if (ageYears is < 18 or > 120)
            throw new ArgumentOutOfRangeException(nameof(ageYears));
        if (!Enum.IsDefined(sex))
            throw new ArgumentOutOfRangeException(nameof(sex));

        var adjustment = sex switch
        {
            EquationSex.Male => 5,
            EquationSex.Female => -161,
            _ => throw new ArgumentOutOfRangeException(nameof(sex))
        };
        var bmr = 10 * weightKilograms + 6.25 * heightCentimeters - 5 * ageYears + adjustment;
        var activityMultiplier = activityLevel switch
        {
            ActivityLevel.Sedentary => 1.2,
            ActivityLevel.Light => 1.375,
            ActivityLevel.Moderate => 1.55,
            ActivityLevel.High => 1.725,
            ActivityLevel.Athlete => 1.9,
            _ => throw new ArgumentOutOfRangeException(nameof(activityLevel))
        };
        var tdee = bmr * activityMultiplier;
        var target = goal switch
        {
            CalorieGoal.Maintain => tdee,
            CalorieGoal.FatLoss => tdee - 500,
            CalorieGoal.MuscleGain => tdee + 300,
            _ => throw new ArgumentOutOfRangeException(nameof(goal))
        };

        return new CalorieEstimate(
            Math.Round(bmr, MidpointRounding.AwayFromZero),
            Math.Round(tdee, MidpointRounding.AwayFromZero),
            Math.Round(target, MidpointRounding.AwayFromZero));
    }
}
