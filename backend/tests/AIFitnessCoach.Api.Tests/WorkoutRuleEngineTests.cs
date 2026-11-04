using AIFitnessCoach.AI.Services;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class WorkoutRuleEngineTests
{
    [Theory]
    [InlineData("5", "10", 7, false)]
    [InlineData("3", "10", 7, true)]
    [InlineData("2", "25", 7, false)]
    [InlineData("3", "10", 5, false)]
    public void RuleEngineEnforcesVolumeIntensityAndRecovery(
        string sets, string reps, double sleep, bool expected)
    {
        var catalog = new ExerciseCatalog();
        Assert.True(catalog.TryGet("bodyweight-squat", out var exercise));
        var input = WorkoutPlannerTests.Input(
            new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 40, sleep, 0.8);
        var draft = new WorkoutExerciseDraft("bodyweight-squat", exercise.Name, sets, reps, 10, 60, null);

        Assert.Equal(expected, new WorkoutRuleEngine().IsIntensityAllowed(draft, exercise, input));
    }
}
