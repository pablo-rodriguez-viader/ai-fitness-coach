using AIFitnessCoach.AI.Services;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class WorkoutPlannerTests
{
    [Fact]
    public void PlannerSelectsOnlyExercisesMatchingEquipmentAndGoal()
    {
        var catalog = new ExerciseCatalog();
        var planner = new WorkoutPlanner(catalog);
        var input = Input(new HashSet<ExerciseEquipment> { ExerciseEquipment.Bodyweight }, 20, sleep: 7, consistency: 0.8);

        var exercises = planner.SelectExercises(input, "general_health");

        Assert.NotEmpty(exercises);
        Assert.All(exercises, exercise => Assert.All(exercise.Equipment, item => Assert.Contains(item, input.AvailableEquipment)));
        Assert.DoesNotContain(exercises, item => item.Id == "dumbbell-row");
    }

    internal static WorkoutPlanningInput Input(
        IReadOnlySet<ExerciseEquipment> equipment,
        int minutes,
        double? sleep,
        double consistency) =>
        new(34, 70, 170, "moderate", sleep, consistency, minutes, equipment);
}
