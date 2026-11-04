using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Application.UseCases.AI;

namespace AIFitnessCoach.Infrastructure.AI;

public sealed class ActivityAnalysisContextBuilder(
    IAIContextSource contextSource,
    IWorkoutRepository workoutRepository,
    HealthSummaryAnalyzer summaryAnalyzer,
    HealthTrendAnalyzer trendAnalyzer,
    ProgressAnalyzer progressAnalyzer,
    TimeProvider timeProvider) : IActivityAnalysisContextBuilder
{
    public async Task<ActivityAnalysisContext> BuildAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await contextSource.GetContextAsync(userId, now.AddDays(-90), cancellationToken);
        var recentWorkouts = await workoutRepository.GetHistoryAsync(userId, 10, cancellationToken);
        var currentWeek = summaryAnalyzer.BuildWeekly(user.RecentMetrics, now);
        var previousWeek = summaryAnalyzer.BuildWeekly(user.RecentMetrics, now, daysOffset: 7);
        var currentMonth = summaryAnalyzer.BuildMonthly(user.RecentMetrics, now);
        var trend = trendAnalyzer.Analyze(user.RecentMetrics, now);
        var progress = progressAnalyzer.Analyze(currentWeek, previousWeek, user.Goal);

        return new ActivityAnalysisContext(
            user.Age is null ? null : new ActivityAnalysisProfile(user.Age),
            user.Nutrition?.ActiveGoal ?? user.Goal,
            ToPeriod(currentWeek),
            ToPeriod(previousWeek),
            ToPeriod(currentMonth),
            [
                new ActivityMetricTrend("weight", trend.WeightChangeKilograms, "kg"),
                new ActivityMetricTrend("steps", trend.AverageDailyStepsChangePercent, "%"),
                new ActivityMetricTrend("sleep", trend.AverageSleepChangeHours, "hours"),
                new ActivityMetricTrend("workouts", trend.WorkoutCountChange, "sessions")
            ],
            progress.ConsistencyLabel,
            progress.ActiveDaysLast7Days,
            progress.ActiveDaysPrevious7Days,
            recentWorkouts.Select(workout => new ActivityWorkoutSummary(
                workout.Date,
                workout.Goal,
                workout.Completed,
                workout.Difficulty)).ToArray());
    }

    private static ActivityPeriodSummary ToPeriod(WeeklyHealthSummary summary) =>
        new(
            summary.PeriodStart,
            summary.PeriodEnd,
            summary.Steps,
            summary.AverageSleepHours,
            summary.WeightKilograms,
            summary.Workouts,
            summary.WorkoutMinutes,
            summary.AverageHeartRate,
            summary.DaysWithSteps,
            summary.DaysWithWorkouts);

    private static ActivityPeriodSummary ToPeriod(MonthlyHealthSummary summary) =>
        new(
            summary.PeriodStart,
            summary.PeriodEnd,
            summary.Steps,
            summary.AverageSleepHours,
            summary.WeightKilograms,
            summary.Workouts,
            summary.WorkoutMinutes,
            summary.AverageHeartRate,
            summary.DaysWithSteps,
            summary.DaysWithWorkouts);
}
