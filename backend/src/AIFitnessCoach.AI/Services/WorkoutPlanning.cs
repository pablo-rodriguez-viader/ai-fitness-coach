using AIFitnessCoach.AI.Contracts;

namespace AIFitnessCoach.AI.Services;

public enum ExerciseCategory { Strength, Cardio, Mobility, Recovery }
public enum ExerciseEquipment { Bodyweight, Dumbbells, ResistanceBand, Mat, Treadmill }
public enum ExerciseLevel { Beginner, Intermediate }

public sealed record Exercise(
    string Id,
    string Name,
    string Description,
    string MuscleGroup,
    ExerciseLevel Level,
    IReadOnlySet<ExerciseEquipment> Equipment,
    ExerciseCategory Category,
    int EstimatedMinutes,
    string? VideoUrl,
    IReadOnlySet<string> Restrictions);

public sealed record WorkoutTemplate(string Id, string Goal, IReadOnlyList<string> ExerciseIds);

public sealed record WorkoutPlanDraft(string Goal, IReadOnlyList<WorkoutDayDraft> Days);
public sealed record WorkoutDayDraft(string Title, string? RecoveryNote, IReadOnlyList<WorkoutExerciseDraft> Exercises);
public sealed record WorkoutExerciseDraft(
    string ExerciseId,
    string Name,
    string? Sets,
    string? Repetitions,
    int? DurationMinutes,
    int? RestSeconds,
    string? Instructions);

public sealed record WorkoutPlanningInput(
    int? Age,
    double? WeightKilograms,
    double? HeightCentimeters,
    string? ActivityLevel,
    double? AverageSleepHours,
    double? ConsistencyRate,
    int AvailableMinutes,
    IReadOnlySet<ExerciseEquipment> AvailableEquipment);

public sealed class ExerciseCatalog
{
    private static readonly IReadOnlySet<ExerciseEquipment> NoEquipment = new HashSet<ExerciseEquipment>
    {
        ExerciseEquipment.Bodyweight
    };

    private static readonly IReadOnlySet<ExerciseEquipment> MatEquipment = new HashSet<ExerciseEquipment>
    {
        ExerciseEquipment.Bodyweight, ExerciseEquipment.Mat
    };

    private static readonly IReadOnlySet<ExerciseEquipment> DumbbellEquipment = new HashSet<ExerciseEquipment>
    {
        ExerciseEquipment.Dumbbells
    };

    private readonly IReadOnlyDictionary<string, Exercise> exercises = new Dictionary<string, Exercise>(StringComparer.OrdinalIgnoreCase)
    {
        ["bodyweight-squat"] = new("bodyweight-squat", "Sentadilla", "Sentadilla controlada con peso corporal.", "legs", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Strength, 5, null, new HashSet<string>()),
        ["glute-bridge"] = new("glute-bridge", "Puente de glúteos", "Elevación de cadera tumbado en el suelo.", "glutes", ExerciseLevel.Beginner, MatEquipment, ExerciseCategory.Strength, 5, null, new HashSet<string>()),
        ["wall-push-up"] = new("wall-push-up", "Flexión en pared", "Flexión inclinada con las manos apoyadas en una pared.", "chest", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Strength, 5, null, new HashSet<string>()),
        ["bird-dog"] = new("bird-dog", "Bird dog", "Extensión alterna de brazo y pierna con control.", "core", ExerciseLevel.Beginner, MatEquipment, ExerciseCategory.Strength, 5, null, new HashSet<string>()),
        ["calf-raise"] = new("calf-raise", "Elevación de gemelos", "Elevación controlada sobre la punta de los pies.", "calves", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Strength, 5, null, new HashSet<string>()),
        ["march-in-place"] = new("march-in-place", "Marcha en el sitio", "Marcha cómoda a ritmo conversacional.", "cardio", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Cardio, 8, null, new HashSet<string>()),
        ["brisk-walk"] = new("brisk-walk", "Caminata ligera", "Caminata a ritmo cómodo y sostenible.", "cardio", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Cardio, 10, null, new HashSet<string>()),
        ["easy-jog"] = new("easy-jog", "Trote suave", "Trote fácil que permite mantener una conversación.", "cardio", ExerciseLevel.Intermediate, NoEquipment, ExerciseCategory.Cardio, 10, null, new HashSet<string> { "running" }),
        ["wall-sit"] = new("wall-sit", "Sentadilla isométrica en pared", "Posición sentada contra la pared durante un intervalo breve.", "legs", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Strength, 4, null, new HashSet<string>()),
        ["dead-bug"] = new("dead-bug", "Dead bug", "Movimiento alterno de extremidades manteniendo el tronco estable.", "core", ExerciseLevel.Beginner, MatEquipment, ExerciseCategory.Strength, 5, null, new HashSet<string>()),
        ["dumbbell-row"] = new("dumbbell-row", "Remo con mancuerna", "Remo con una mancuerna y apoyo estable.", "back", ExerciseLevel.Beginner, DumbbellEquipment, ExerciseCategory.Strength, 6, null, new HashSet<string>()),
        ["dumbbell-press"] = new("dumbbell-press", "Press con mancuernas", "Press ligero con recorrido cómodo.", "chest", ExerciseLevel.Beginner, DumbbellEquipment, ExerciseCategory.Strength, 6, null, new HashSet<string>()),
        ["hamstring-stretch"] = new("hamstring-stretch", "Movilidad posterior de piernas", "Estiramiento suave sin rebotes.", "mobility", ExerciseLevel.Beginner, MatEquipment, ExerciseCategory.Mobility, 4, null, new HashSet<string> { "pain" }),
        ["breathing-recovery"] = new("breathing-recovery", "Respiración de recuperación", "Respiración tranquila para cerrar la sesión.", "recovery", ExerciseLevel.Beginner, NoEquipment, ExerciseCategory.Recovery, 3, null, new HashSet<string>())
    };

    public IReadOnlyCollection<Exercise> All => exercises.Values.ToArray();
    public bool TryGet(string id, out Exercise exercise) => exercises.TryGetValue(id, out exercise!);
    public bool TryResolve(string id, string name, out Exercise exercise)
    {
        if (!string.IsNullOrWhiteSpace(id) && exercises.TryGetValue(id, out exercise!))
            return true;
        exercise = exercises.Values.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))!;
        if (exercise is not null)
            return true;
        if (name.Equals("Sentadillas", StringComparison.OrdinalIgnoreCase))
            return exercises.TryGetValue("bodyweight-squat", out exercise!);
        return false;
    }
}

public sealed class WorkoutPlanner(ExerciseCatalog catalog)
{
    public IReadOnlyList<Exercise> SelectExercises(WorkoutPlanningInput input, string goal)
    {
        var allowed = catalog.All.Where(exercise => exercise.Equipment.All(input.AvailableEquipment.Contains));
        if (input.AverageSleepHours is < 6 || input.ConsistencyRate < 0.35)
            allowed = allowed.Where(exercise => exercise.Level == ExerciseLevel.Beginner
                && (exercise.Category is not ExerciseCategory.Cardio
                    || exercise.Id is "brisk-walk" or "march-in-place"));
        if (goal.Equals("running", StringComparison.OrdinalIgnoreCase))
            allowed = allowed.Where(exercise => exercise.Category == ExerciseCategory.Cardio
                || exercise.Category == ExerciseCategory.Strength
                || exercise.Category == ExerciseCategory.Recovery);
        else if (goal is "muscle_gain" or "maintenance")
            allowed = allowed.Where(exercise => exercise.Category != ExerciseCategory.Recovery);
        return allowed.Take(Math.Clamp(input.AvailableMinutes / 5, 1, 8)).ToArray();
    }

    public string BuildCatalogPrompt(WorkoutPlanningInput input, string goal, int days)
    {
        var allowed = SelectExercises(input, goal);
        return $"Generate exactly {days} days of structured JSON. Use only these catalog exercise ids: "
            + string.Join(", ", allowed.Select(exercise => exercise.Id))
            + $". Goal={goal}; availableMinutes={input.AvailableMinutes}; activityLevel={input.ActivityLevel ?? "unknown"}; "
            + $"age={input.Age?.ToString() ?? "unknown"}; weightKg={input.WeightKilograms?.ToString("0.0") ?? "unknown"}; "
            + $"heightCm={input.HeightCentimeters?.ToString("0") ?? "unknown"}; sleepHours={input.AverageSleepHours?.ToString("0.0") ?? "unknown"}; "
            + $"consistency={input.ConsistencyRate?.ToString("0.00") ?? "unknown"}. Include exerciseId and use catalog names exactly. "
            + "Respect time, equipment, recovery and conservative beginner-safe limits.";
    }
}

public sealed class WorkoutRuleEngine
{
    public const int MaximumExercisesPerDay = 8;
    public const int MaximumSetsPerExercise = 4;
    public const int MinimumRestSeconds = 30;
    public const int MaximumRestSeconds = 180;

    public bool IsIntensityAllowed(WorkoutExerciseDraft exercise, Exercise catalogExercise, WorkoutPlanningInput input)
    {
        if (exercise.Sets is not null && !int.TryParse(exercise.Sets, out _)
            || exercise.Repetitions is not null && !int.TryParse(exercise.Repetitions, out _))
            return false;
        var maxSets = input.AverageSleepHours is < 6 || input.ConsistencyRate < 0.35
            ? 2
            : MaximumSetsPerExercise;
        var sets = int.TryParse(exercise.Sets, out var parsedSets) ? parsedSets : 1;
        var reps = int.TryParse(exercise.Repetitions, out var parsedReps) ? parsedReps : 1;
        return sets is >= 1 && sets <= maxSets && reps is >= 1 and <= 20
            && (catalogExercise.Level != ExerciseLevel.Beginner || sets <= 3);
    }
}

public sealed class WorkoutSafetyValidator(ExerciseCatalog catalog, WorkoutRuleEngine ruleEngine)
{
    public WorkoutPlanDraft Correct(WorkoutPlanDraft draft, WorkoutPlanningInput input)
    {
        var conservative = input.AverageSleepHours is < 6 || input.ConsistencyRate < 0.35;
        return draft with
        {
            Days = draft.Days.Select(day => day with
            {
                Exercises = day.Exercises.Select(item =>
                {
                    if (!catalog.TryResolve(item.ExerciseId, item.Name, out var exercise))
                        return item;
                    var maxSets = conservative ? 2
                        : exercise.Level == ExerciseLevel.Beginner ? 3 : WorkoutRuleEngine.MaximumSetsPerExercise;
                    var sets = int.TryParse(item.Sets, out var count) ? Math.Clamp(count, 1, maxSets).ToString() : item.Sets;
                    var repetitions = int.TryParse(item.Repetitions, out var reps)
                        ? Math.Clamp(reps, 1, 20).ToString()
                        : item.Repetitions;
                    var rest = item.RestSeconds is { } seconds
                        ? Math.Clamp(seconds, WorkoutRuleEngine.MinimumRestSeconds, WorkoutRuleEngine.MaximumRestSeconds)
                        : item.RestSeconds;
                    return item with { Sets = sets, Repetitions = repetitions, RestSeconds = rest };
                }).ToArray()
            }).ToArray()
        };
    }

    public (bool IsValid, string? Error) Validate(
        WorkoutPlanDraft draft,
        string expectedGoal,
        int expectedDays,
        WorkoutPlanningInput input)
    {
        var allowedIds = new WorkoutPlanner(catalog).SelectExercises(input, expectedGoal)
            .Select(exercise => exercise.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!WorkoutGoal.Supported.Contains(draft.Goal) || !draft.Goal.Equals(expectedGoal, StringComparison.OrdinalIgnoreCase))
            return (false, "Plan goal does not match the requested goal.");
        if (draft.Days is null || draft.Days.Count != expectedDays)
            return (false, "Plan day count is invalid.");
        foreach (var day in draft.Days)
        {
            if (string.IsNullOrWhiteSpace(day.Title) || day.Exercises is null || day.Exercises.Count > WorkoutRuleEngine.MaximumExercisesPerDay)
                return (false, "Plan contains an invalid or excessive workout day.");
            var seenMuscles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var estimatedMinutes = 0;
            foreach (var item in day.Exercises)
            {
                if (!catalog.TryResolve(item.ExerciseId, item.Name, out var exercise))
                    return (false, "Plan references an exercise outside the internal catalog.");
                if (!allowedIds.Contains(exercise.Id))
                    return (false, "Plan exercise is not eligible for this goal, recovery state, or equipment.");
                if (!exercise.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                    return (false, "Plan exercise name does not match the internal catalog.");
                if (exercise.Equipment.Any(equipment => !input.AvailableEquipment.Contains(equipment)))
                    return (false, "Plan requires unavailable equipment.");
                if (exercise.Restrictions.Contains("running") && input.AverageSleepHours is < 6)
                    return (false, "Plan intensity is too high for the available recovery data.");
                if (!ruleEngine.IsIntensityAllowed(item, exercise, input))
                    return (false, "Plan volume or intensity exceeds safety limits.");
                if (exercise.Category == ExerciseCategory.Strength && !seenMuscles.Add(exercise.MuscleGroup))
                    return (false, "Plan repeats a muscle group excessively within one day.");
                var sets = int.TryParse(item.Sets, out var setCount) ? setCount : 1;
                var workMinutes = exercise.EstimatedMinutes * sets;
                var restMinutes = item.RestSeconds is { } safeRest ? (safeRest * Math.Max(0, sets - 1)) / 60 : 0;
                estimatedMinutes += Math.Max(item.DurationMinutes ?? 0, workMinutes + restMinutes);
                if (item.RestSeconds is { } rest && rest is < WorkoutRuleEngine.MinimumRestSeconds or > WorkoutRuleEngine.MaximumRestSeconds)
                    return (false, "Plan rest interval is outside the safe range.");
            }
            if (estimatedMinutes > input.AvailableMinutes)
                return (false, "Plan cannot fit the available workout time.");
        }
        return (true, null);
    }
}

public sealed class RecoveryEngine
{
    public bool ShouldReduceLoad(double? averageSleepHours, double? consistencyRate, bool recentFatigue) =>
        averageSleepHours is < 6 || consistencyRate is < 0.35 || recentFatigue;
}

public sealed class ProgressionEngine
{
    public int AdjustSets(
        int sets,
        bool lowSleep,
        bool lowAdherence,
        bool highFatigue,
        bool positiveAdherence,
        int maximumSets = WorkoutRuleEngine.MaximumSetsPerExercise)
    {
        if (lowSleep || lowAdherence || highFatigue)
            return Math.Max(1, sets - 1);
        if (positiveAdherence)
            return Math.Min(maximumSets, sets + 1);
        return Math.Clamp(sets, 1, maximumSets);
    }
}
