using AIFitnessCoach.AI.Services;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class WorkoutValidatorTests
{
    [Fact]
    public void ValidatorRejectsExercisesOutsideTheInternalCatalog()
    {
        var catalog = new ExerciseCatalog();
        var validator = new WorkoutSafetyValidator(catalog, new WorkoutRuleEngine());
        var input = WorkoutPlannerTests.Input(new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 30, 7, 0.8);
        var draft = Draft(new WorkoutExerciseDraft("custom-id", "Burpees extremos", "8", "50", 40, 10, null));

        var result = validator.Validate(draft, "general_health", 1, input);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidatorRejectsDuplicateMuscleGroupsAndSessionsThatExceedAvailableTime()
    {
        var catalog = new ExerciseCatalog();
        var validator = new WorkoutSafetyValidator(catalog, new WorkoutRuleEngine());
        var input = WorkoutPlannerTests.Input(new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 60, 7, 0.8);
        var exercises = new[]
        {
            new WorkoutExerciseDraft("bodyweight-squat", "Sentadilla", "3", "10", 8, 60, null),
            new WorkoutExerciseDraft("wall-sit", "Sentadilla isométrica en pared", "2", "10", 8, 60, null)
        };
        var result = validator.Validate(Draft(exercises), "general_health", 1, input);

        Assert.False(result.IsValid);
        Assert.Contains("muscle group", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatorRejectsDurationThatCannotFitTheAvailableTime()
    {
        var catalog = new ExerciseCatalog();
        var validator = new WorkoutSafetyValidator(catalog, new WorkoutRuleEngine());
        var input = WorkoutPlannerTests.Input(new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 20, 7, 0.8);
        var draft = Draft(new WorkoutExerciseDraft("bodyweight-squat", "Sentadilla", "2", "10", 30, 60, null));

        var result = validator.Validate(draft, "general_health", 1, input);

        Assert.False(result.IsValid);
        Assert.Contains("available workout time", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatorCorrectsExcessVolumeForPoorRecoveryBeforeRevalidation()
    {
        var catalog = new ExerciseCatalog();
        var validator = new WorkoutSafetyValidator(catalog, new WorkoutRuleEngine());
        var input = WorkoutPlannerTests.Input(new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 30, 5, 0.8);
        var original = Draft(new WorkoutExerciseDraft("bodyweight-squat", "Sentadilla", "4", "10", 8, 600, null));

        var corrected = validator.Correct(original, input);
        var exercise = Assert.Single(Assert.Single(corrected.Days).Exercises);

        Assert.Equal("2", exercise.Sets);
        Assert.Equal(WorkoutRuleEngine.MaximumRestSeconds, exercise.RestSeconds);
        Assert.True(validator.Validate(corrected, "general_health", 1, input).IsValid);
    }

    [Fact]
    public void PruneDropsOnlyTheExercisesThatBreakTheRules()
    {
        var catalog = new ExerciseCatalog();
        var validator = new WorkoutSafetyValidator(catalog, new WorkoutRuleEngine());
        var input = WorkoutPlannerTests.Input(new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 60, 7, 0.8);
        var draft = Draft(
            new WorkoutExerciseDraft("bodyweight-squat", "Sentadilla", "3", "10", 8, 60, null),
            new WorkoutExerciseDraft("wall-sit", "Sentadilla isométrica en pared", "2", "10", 8, 60, null),
            new WorkoutExerciseDraft("custom-id", "Burpees extremos", "8", "50", 40, 10, null));

        var pruned = validator.Prune(draft, "general_health", input);

        var exercise = Assert.Single(Assert.Single(pruned.Days).Exercises);
        Assert.Equal("bodyweight-squat", exercise.ExerciseId);
        Assert.True(validator.Validate(pruned, "general_health", 1, input).IsValid);
    }

    private static WorkoutPlanDraft Draft(params WorkoutExerciseDraft[] exercises) =>
        new("general_health", [new WorkoutDayDraft("Sesión", "", exercises)]);
}
