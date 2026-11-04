using System.Text.Json;

namespace AIFitnessCoach.Application.UseCases.AI;

public sealed class CoachContextSelector
{
    private static readonly string[] AllCategories =
    [
        "goal", "weekly", "monthly", "consistency", "workouts",
        "weeklySteps", "stepTrend", "weeklySleep", "sleepTrend",
        "weightTrend", "weightMeasurementCount", "dataAvailability",
        "followUpContext"
    ];

    public CoachContextSelection Select(
        ActivityAnalysisContext context,
        CoachIntent intent,
        string requestedPeriod)
    {
        var policy = CoachIntentClassifier.PolicyFor(intent);
        var period = requestedPeriod == "auto" ? policy.DefaultPeriod : requestedPeriod;
        var data = new Dictionary<string, object?>();
        var included = new List<string>();
        var missing = new List<string>();
        var inclusionReasons = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string category, object? value, bool available = true)
        {
            included.Add(category);
            inclusionReasons[category] = $"Required for {intent}.";
            data[category] = value;
            if (!available)
                missing.Add(category);
        }

        switch (intent)
        {
            case CoachIntent.WeeklyActivityAnalysis:
                Add("goal", context.ActiveGoal);
                Add("weekly", new
                    {
                        current = new
                        {
                            context.CurrentWeek.Steps,
                            context.CurrentWeek.Workouts,
                            context.CurrentWeek.WorkoutMinutes,
                            context.CurrentWeek.DaysWithSteps,
                            context.CurrentWeek.DaysWithWorkouts
                        },
                        previous = new
                        {
                            context.PreviousWeek.Steps,
                            context.PreviousWeek.Workouts,
                            context.PreviousWeek.WorkoutMinutes,
                            context.PreviousWeek.DaysWithSteps,
                            context.PreviousWeek.DaysWithWorkouts
                        }
                    },
                    context.CurrentWeek.DaysWithSteps > 0 || context.CurrentWeek.DaysWithWorkouts > 0);
                Add("consistency", new { context.ActiveDaysLastWeek, context.ActiveDaysPreviousWeek },
                    context.ActiveDaysLastWeek + context.ActiveDaysPreviousWeek > 0);
                Add("workouts", context.RecentWorkouts.Take(6).ToArray(), context.RecentWorkouts.Count > 0);
                break;
            case CoachIntent.MonthlyProgressAnalysis:
                Add("goal", context.ActiveGoal);
                Add("monthly", new
                    {
                        context.CurrentMonth.PeriodStart,
                        context.CurrentMonth.PeriodEnd,
                        context.CurrentMonth.Steps,
                        context.CurrentMonth.Workouts,
                        context.CurrentMonth.WorkoutMinutes,
                        context.CurrentMonth.DaysWithSteps,
                        context.CurrentMonth.DaysWithWorkouts
                    }, context.CurrentMonth.DaysWithSteps > 0
                    || context.CurrentMonth.DaysWithWorkouts > 0);
                Add("trends", context.Trends, context.Trends.Count > 0);
                break;
            case CoachIntent.StepTrendAnalysis:
                if (period == "last30Days")
                    Add("weeklySteps", new { current = context.CurrentMonth.Steps, previous = (double?)null,
                        currentDays = context.CurrentMonth.DaysWithSteps, previousDays = 0 },
                        context.CurrentMonth.DaysWithSteps > 0);
                else
                    Add("weeklySteps", new { current = context.CurrentWeek.Steps, previous = context.PreviousWeek.Steps,
                        currentDays = context.CurrentWeek.DaysWithSteps, previousDays = context.PreviousWeek.DaysWithSteps },
                        context.CurrentWeek.DaysWithSteps > 0 || context.PreviousWeek.DaysWithSteps > 0);
                Add("stepTrend", context.Trends.FirstOrDefault(item => item.Metric == "steps"),
                    context.Trends.Any(item => item.Metric == "steps" && item.Change.HasValue));
                break;
            case CoachIntent.SleepTrendAnalysis:
                if (period == "last30Days")
                    Add("weeklySleep", new { current = context.CurrentMonth.AverageSleepHours,
                        previous = (double?)null },
                        context.CurrentMonth.AverageSleepHours.HasValue);
                else
                    Add("weeklySleep", new { current = context.CurrentWeek.AverageSleepHours,
                        previous = context.PreviousWeek.AverageSleepHours },
                        context.CurrentWeek.AverageSleepHours.HasValue || context.PreviousWeek.AverageSleepHours.HasValue);
                Add("sleepTrend", context.Trends.FirstOrDefault(item => item.Metric == "sleep"),
                    context.Trends.Any(item => item.Metric == "sleep" && item.Change.HasValue));
                break;
            case CoachIntent.WeightTrendAnalysis:
                var currentWeight = period == "last30Days"
                    ? context.CurrentMonth.WeightKilograms
                    : context.CurrentWeek.WeightKilograms;
                var previousWeight = period == "last30Days"
                    ? (double?)null
                    : context.PreviousWeek.WeightKilograms;
                Add("weightTrend", new { current = currentWeight,
                    previous = previousWeight,
                    trend = context.Trends.FirstOrDefault(item => item.Metric == "weight") },
                    currentWeight.HasValue || previousWeight.HasValue);
                Add("weightDataAvailability", new { currentAvailable = currentWeight.HasValue,
                    previousAvailable = previousWeight.HasValue },
                    currentWeight.HasValue || previousWeight.HasValue);
                break;
            case CoachIntent.WorkoutConsistencyAnalysis:
                Add("consistency", new
                    {
                        context.Consistency,
                        context.ActiveDaysLastWeek,
                        context.ActiveDaysPreviousWeek,
                        currentWorkouts = context.CurrentWeek.Workouts,
                        previousWorkouts = context.PreviousWeek.Workouts
                    },
                    context.CurrentWeek.DaysWithWorkouts > 0 || context.PreviousWeek.DaysWithWorkouts > 0);
                Add("workouts", context.RecentWorkouts.Take(6).ToArray(), context.RecentWorkouts.Count > 0);
                break;
            case CoachIntent.DataAvailabilityQuestion:
                Add("dataAvailability", new
                {
                    stepsDays = context.CurrentWeek.DaysWithSteps,
                    workoutDays = context.CurrentWeek.DaysWithWorkouts,
                    sleepAvailable = context.CurrentWeek.AverageSleepHours.HasValue,
                    weightAvailable = context.CurrentWeek.WeightKilograms.HasValue,
                    recentWorkouts = context.RecentWorkouts.Count
                });
                break;
            case CoachIntent.ConversationFollowUp:
                Add("goal", context.ActiveGoal);
                Add("followUpContext", new
                    {
                        context.Consistency,
                        currentSteps = context.CurrentWeek.Steps,
                        previousSteps = context.PreviousWeek.Steps,
                        currentWorkouts = context.CurrentWeek.Workouts,
                        previousWorkouts = context.PreviousWeek.Workouts
                    });
                break;
            default:
                Add("goal", context.ActiveGoal);
                Add("weekly", new
                    {
                        steps = context.CurrentWeek.Steps,
                        workoutCount = context.CurrentWeek.Workouts,
                        activeDays = context.ActiveDaysLastWeek
                    }, context.CurrentWeek.DaysWithSteps > 0
                    || context.CurrentWeek.DaysWithWorkouts > 0);
                break;
        }

        var omitted = AllCategories.Except(included, StringComparer.Ordinal).ToArray();
        data["missingData"] = missing;
        data["selection"] = new
        {
            includedCategories = included,
            omittedCategories = omitted,
            inclusionReasons,
            unavailableCategories = missing,
            period
        };
        return new CoachContextSelection(
            intent,
            included,
            omitted,
            missing,
            JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            period,
            period == "last30Days" ? context.CurrentMonth.PeriodStart : context.CurrentWeek.PeriodStart,
            period == "last30Days" ? context.CurrentMonth.PeriodEnd : context.CurrentWeek.PeriodEnd,
            CoachPromptBuilder.ContextVersion);
    }
}
