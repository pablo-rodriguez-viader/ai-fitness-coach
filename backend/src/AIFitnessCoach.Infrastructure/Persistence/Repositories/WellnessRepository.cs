using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class WellnessRepository(FitnessDbContext dbContext) : IWellnessRepository
{
    public async Task<NutritionUserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.UserProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => new NutritionUserProfile(profile.Age, profile.HeightCentimeters))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<NutritionProfile?> GetNutritionProfileAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.NutritionProfiles.SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task SaveNutritionProfileAsync(NutritionProfile profile, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(profile).State == EntityState.Detached)
            dbContext.NutritionProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Goal?> GetActiveGoalAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Goals
            .Include(goal => goal.Progress)
            .Where(goal => goal.UserId == userId && goal.Status == GoalStatus.Active)
            .OrderByDescending(goal => goal.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Goal>> GetGoalsAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.Goals
            .AsNoTracking()
            .Where(goal => goal.UserId == userId)
            .OrderByDescending(goal => goal.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task AddGoalAsync(Goal goal, CancellationToken cancellationToken)
    {
        dbContext.Goals.Add(goal);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PauseActiveGoalsAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var goals = await dbContext.Goals
            .Where(goal => goal.UserId == userId && goal.Status == GoalStatus.Active)
            .ToArrayAsync(cancellationToken);
        foreach (var goal in goals)
            goal.SetStatus(GoalStatus.Paused, now);
        if (goals.Length > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveMacroTargetAsync(MacroTarget target, CancellationToken cancellationToken)
    {
        dbContext.MacroTargets.Add(target);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<MacroTarget?> GetMacroTargetAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        dbContext.MacroTargets
            .AsNoTracking()
            .Where(target => target.UserId == userId && target.EffectiveFrom <= date)
            .OrderByDescending(target => target.EffectiveFrom)
            .ThenByDescending(target => target.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddNutritionLogAsync(NutritionLog log, CancellationToken cancellationToken)
    {
        var summary = await dbContext.DailyNutritionSummaries
            .SingleOrDefaultAsync(
                item => item.UserId == log.UserId && item.Date == log.Date,
                cancellationToken);
        var logs = await dbContext.NutritionLogs
            .Where(item => item.UserId == log.UserId && item.Date == log.Date)
            .ToArrayAsync(cancellationToken);
        var allLogs = logs.Append(log).ToArray();
        dbContext.NutritionLogs.Add(log);
        if (summary is null)
        {
            summary = DailyNutritionSummary.Create(
                log.UserId,
                log.Date,
                allLogs.Sum(item => item.Calories),
                allLogs.Sum(item => item.ProteinGrams),
                allLogs.Sum(item => item.FatGrams),
                allLogs.Sum(item => item.CarbohydrateGrams),
                log.CreatedAt);
            dbContext.DailyNutritionSummaries.Add(summary);
        }
        else
        {
            summary.Recalculate(
                allLogs.Sum(item => item.Calories),
                allLogs.Sum(item => item.ProteinGrams),
                allLogs.Sum(item => item.FatGrams),
                allLogs.Sum(item => item.CarbohydrateGrams),
                log.CreatedAt);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NutritionLog>> GetNutritionLogsAsync(
        string userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        await dbContext.NutritionLogs
            .AsNoTracking()
            .Where(log => log.UserId == userId && log.Date >= from && log.Date <= to)
            .OrderBy(log => log.CreatedAt)
            .Take(5_000)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<DailyNutritionSummary>> GetNutritionSummariesAsync(
        string userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        await dbContext.DailyNutritionSummaries
            .AsNoTracking()
            .Where(summary => summary.UserId == userId && summary.Date >= from && summary.Date <= to)
            .OrderBy(summary => summary.Date)
            .Take(366)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<AIHealthMetricContext>> GetWeightMetricsAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken) =>
        await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId && metric.Metric == "body_mass" && metric.ObservedAt >= from)
            .OrderByDescending(metric => metric.ObservedAt)
            .Take(2_000)
            .Select(metric => new AIHealthMetricContext(metric.Metric, metric.Value, metric.Unit, metric.ObservedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<AIHealthMetricContext>> GetProgressMetricsAsync(
        string userId,
        DateTimeOffset from,
        CancellationToken cancellationToken) =>
        await dbContext.HealthMetrics
            .AsNoTracking()
            .Where(metric => metric.UserId == userId
                && metric.ObservedAt >= from
                && (metric.Metric == "body_mass" || metric.Metric == "steps" || metric.Metric == "sleep_duration"))
            .OrderByDescending(metric => metric.ObservedAt)
            .Take(20_000)
            .Select(metric => new AIHealthMetricContext(metric.Metric, metric.Value, metric.Unit, metric.ObservedAt))
            .ToArrayAsync(cancellationToken);

    public async Task SaveGoalProgressAsync(GoalProgress progress, CancellationToken cancellationToken)
    {
        var existing = await dbContext.GoalProgress
            .SingleOrDefaultAsync(
                item => item.GoalId == progress.GoalId && item.Date == progress.Date,
                cancellationToken);
        if (existing is null)
            dbContext.GoalProgress.Add(progress);
        else
            existing.Update(
                progress.WeightKilograms,
                progress.CompletionPercent,
                progress.Steps,
                progress.SleepHours,
                progress.CreatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GoalProgress>> GetGoalProgressAsync(
        Guid goalId,
        CancellationToken cancellationToken) =>
        await dbContext.GoalProgress
            .AsNoTracking()
            .Where(progress => progress.GoalId == goalId)
            .OrderBy(progress => progress.Date)
            .Take(366)
            .ToArrayAsync(cancellationToken);
}
