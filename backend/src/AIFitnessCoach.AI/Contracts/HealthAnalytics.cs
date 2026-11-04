namespace AIFitnessCoach.AI.Contracts;

public sealed record WeeklyHealthSummary(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    double? Steps,
    double? AverageSleepHours,
    double? WeightKilograms,
    int? Workouts,
    double? WorkoutMinutes,
    double? AverageHeartRate,
    int DaysWithSteps,
    int DaysWithWorkouts);

public sealed record MonthlyHealthSummary(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    double? Steps,
    double? AverageSleepHours,
    double? WeightKilograms,
    int? Workouts,
    double? WorkoutMinutes,
    double? AverageHeartRate,
    int DaysWithSteps,
    int DaysWithWorkouts);

public sealed record HealthTrendSummary(
    double? WeightChangeKilograms,
    double? WeightChangePercent,
    double? AverageDailyStepsChangePercent,
    double? AverageSleepChangeHours,
    double? WorkoutCountChange);

public sealed record ProgressSummary(
    string? ConsistencyLabel,
    string? GoalAlignment,
    string? ImprovementSuggestion,
    int ActiveDaysLast7Days,
    int ActiveDaysPrevious7Days);
