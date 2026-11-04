using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class CoachV3Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");

    [Fact]
    public void TrendAnalyzerComparesWeeklyWeightActivitySleepHeartRateAndCalories()
    {
        var metrics = new List<AIHealthMetricContext>();
        AddMetric(metrics, "body_mass", 80, Now.AddDays(-12));
        AddMetric(metrics, "body_mass", 79, Now.AddDays(-2));
        AddMetric(metrics, "steps", 1000, Now.AddDays(-10));
        AddMetric(metrics, "steps", 1000, Now.AddDays(-9));
        AddMetric(metrics, "steps", 2000, Now.AddDays(-2));
        AddMetric(metrics, "steps", 2000, Now.AddDays(-1));
        AddMetric(metrics, "sleep_duration", 7 * 3600, Now.AddDays(-10));
        AddMetric(metrics, "sleep_duration", 6 * 3600, Now.AddDays(-1));
        AddMetric(metrics, "heart_rate", 75, Now.AddDays(-10));
        AddMetric(metrics, "heart_rate", 70, Now.AddDays(-1));
        AddMetric(metrics, "active_energy", 400, Now.AddDays(-10));
        AddMetric(metrics, "active_energy", 500, Now.AddDays(-1));
        AddMetric(metrics, "total_energy", 2200, Now.AddDays(-10));
        AddMetric(metrics, "total_energy", 2300, Now.AddDays(-1));

        var trends = new TrendAnalyzer().Analyze(metrics, Now).ToDictionary(item => item.Metric);

        Assert.Equal(-1, trends["weight"].Change);
        Assert.Equal(1000, trends["steps"].Change);
        Assert.Equal(-1, trends["sleep"].Change);
        Assert.Equal(-5, trends["heart_rate"].Change);
        Assert.Equal(100, trends["active_calories"].Change);
        Assert.Equal(100, trends["total_calories"].Change);
    }

    [Fact]
    public void AdherenceAnalyzerCountsCompletedPlannedSessionsInLastFourWeeks()
    {
        var firstDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-4);
        var firstDay = WorkoutDay.Create(firstDate, "Strength", "", []);
        var secondDay = WorkoutDay.Create(firstDate.AddDays(1), "Recovery", "", []);
        var plan = WorkoutPlan.Create("user-1", "general_health", firstDate, secondDay.Date, [firstDay, secondDay], Now);
        var history = new[]
        {
            new WorkoutHistory(
                Guid.NewGuid(),
                plan.Id,
                firstDay.Id,
                plan.Goal,
                firstDay.Date,
                firstDay.Title,
                true,
                "Appropriate",
                Now)
        };

        var adherence = new AdherenceAnalyzer().Analyze("user-1", [plan], history, Now);

        Assert.Equal(2, adherence.PlannedSessions);
        Assert.Equal(1, adherence.CompletedSessions);
        Assert.Equal(0.5, adherence.Rate);
    }

    [Fact]
    public void TrendAnalyzerDerivesTotalCaloriesFromActiveAndBasalEnergy()
    {
        var trends = new TrendAnalyzer().Analyze(
            [
                new AIHealthMetricContext("active_energy", 400, "kcal", Now.AddDays(-10)),
                new AIHealthMetricContext("basal_energy", 1600, "kcal", Now.AddDays(-10)),
                new AIHealthMetricContext("active_energy", 500, "kcal", Now.AddDays(-1)),
                new AIHealthMetricContext("basal_energy", 1600, "kcal", Now.AddDays(-1))
            ],
            Now);

        Assert.Equal(100, Assert.Single(trends, item => item.Metric == "total_calories").Change);
    }

    [Fact]
    public void BehaviorAnalyzerFindsRecoveryAndTrainingPatternsFromHistory()
    {
        var adherence = TrainingAdherence.Create(
            "user-1",
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-27),
            DateOnly.FromDateTime(Now.UtcDateTime),
            4,
            1,
            Now);
        var feedback = new[]
        {
            WorkoutFeedback.Create("user-1", Guid.NewGuid(), false, WorkoutDifficulty.VeryHard, null, Now),
            WorkoutFeedback.Create("user-1", Guid.NewGuid(), true, WorkoutDifficulty.VeryEasy, null, Now),
            WorkoutFeedback.Create("user-1", Guid.NewGuid(), false, WorkoutDifficulty.Appropriate, null, Now)
        };
        var trends = new[] { new MetricTrendView("sleep", -1, "hours") };

        var patterns = new BehaviorAnalyzer().Analyze("user-1", adherence, trends, feedback, Now);

        Assert.Contains(patterns, item => item.Key == "training_adherence");
        Assert.Contains(patterns, item => item.Key == "recovery_and_adherence");
        Assert.Contains(patterns, item => item.Key == "workout_difficulty_fit");
    }

    [Fact]
    public void GoalAchievementAnalyzerEstimatesProgressAndPreservesCompletedGoals()
    {
        var goal = Goal.Create(
            "user-1",
            GoalType.FatLoss,
            70,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-20),
            null,
            Now);
        var completedGoal = Goal.Create(
            "user-1",
            GoalType.GeneralHealth,
            null,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30),
            null,
            Now);
        completedGoal.SetStatus(GoalStatus.Completed, Now);

        var results = new GoalAchievementAnalyzer().Analyze(
            [goal, completedGoal],
            [
                new AIHealthMetricContext("body_mass", 80, "kg", Now.AddDays(-20)),
                new AIHealthMetricContext("body_mass", 78, "kg", Now)
            ]);

        Assert.Equal(20, results[0].ProgressPercent);
        Assert.Equal(100, results[1].ProgressPercent);
    }

    [Fact]
    public void ProgressAnalyzerOffersEvidenceBoundedTrendSummary()
    {
        var analyzer = new ProgressAnalyzer();

        var summary = analyzer.AnalyzeCoachProgress(
            [new MetricTrendView("steps", 500, "steps/day")],
            []);

        Assert.Contains("mejora", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("graduales", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("datos suficientes", analyzer.AnalyzeCoachProgress([], []), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkoutFeedbackPersistsRecommendationOutcomeAndScore()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        await dbContext.SaveChangesAsync();
        var repository = new WorkoutRepository(dbContext);
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        var day = WorkoutDay.Create(date, "Session", "", []);
        var plan = WorkoutPlan.Create("user-1", "general_health", date, date, [day], Now);
        await repository.SavePlanAsync(plan, CancellationToken.None);
        await repository.AddRecommendationAsync(
            WorkoutRecommendation.Create("user-1", plan.Id, "training", "Mantén el ritmo.", Now),
            CancellationToken.None);

        await repository.AddFeedbackAsync(
            WorkoutFeedback.Create("user-1", day.Id, true, WorkoutDifficulty.Appropriate, null, Now),
            WorkoutCompletion.Create("user-1", day.Id, true, Now),
            CancellationToken.None);

        var outcome = Assert.Single(await dbContext.RecommendationOutcomes.ToArrayAsync());
        Assert.True(outcome.Completed);
        Assert.Equal(0.9, outcome.Score);
        Assert.StartsWith("workout_feedback:", outcome.Feedback);
    }

    [Fact]
    public async Task InsightRepositoryRefreshesSameDaySnapshotsWithoutDuplicatingRows()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        var goal = Goal.Create(
            "user-1",
            GoalType.GeneralHealth,
            null,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-10),
            null,
            Now);
        dbContext.Goals.Add(goal);
        await dbContext.SaveChangesAsync();
        var repository = new CoachInsightsRepository(dbContext);
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        var start = date.AddDays(-27);

        await repository.SaveAnalysisAsync(
            UserInsights.Create("user-1", date, null, null, null, null, null, null, 0.5, "old", Now),
            [BehaviorPattern.Create("user-1", "consistency", "Old pattern", 0.5, 2, Now)],
            TrainingAdherence.Create("user-1", start, date, 4, 2, Now),
            [GoalAchievement.Create("user-1", goal.Id, "GeneralHealth", "Active", 10, Now)],
            CancellationToken.None);
        await repository.SaveAnalysisAsync(
            UserInsights.Create("user-1", date, null, null, null, null, null, null, 1, "updated", Now.AddMinutes(1)),
            [BehaviorPattern.Create("user-1", "consistency", "Updated pattern", 0.9, 4, Now.AddMinutes(1))],
            TrainingAdherence.Create("user-1", start, date, 4, 4, Now.AddMinutes(1)),
            [GoalAchievement.Create("user-1", goal.Id, "GeneralHealth", "Active", 75, Now.AddMinutes(1))],
            CancellationToken.None);

        Assert.Single(await dbContext.UserInsights.ToArrayAsync());
        Assert.Single(await dbContext.BehaviorPatterns.ToArrayAsync());
        Assert.Single(await dbContext.TrainingAdherence.ToArrayAsync());
        Assert.Single(await dbContext.GoalAchievements.ToArrayAsync());
        Assert.Equal("updated", (await dbContext.UserInsights.SingleAsync()).Summary);
        Assert.Equal(1, (await dbContext.TrainingAdherence.SingleAsync()).Rate);
        Assert.Equal(75, (await dbContext.GoalAchievements.SingleAsync()).ProgressPercent);
    }

    [Fact]
    public void RecommendationOutcomeConstrainsScoreToNormalizedRange()
    {
        var recommendationId = Guid.NewGuid();

        var outcome = RecommendationOutcome.Create(
            "user-1",
            recommendationId,
            true,
            "Helpful",
            1.2,
            Now);

        Assert.Equal(1, outcome.Score);
        Assert.Equal(recommendationId, outcome.RecommendationHistoryId);
    }

    [Fact]
    public void RecommendationScoreConvertsRatingsAndClassifiesEffectiveOutcomes()
    {
        Assert.Equal(0.8, RecommendationScore.FromRating(4).Value);
        Assert.True(RecommendationScore.FromRating(3).IsEffective);
        Assert.False(RecommendationScore.FromRating(2).IsEffective);
        Assert.Throws<ArgumentOutOfRangeException>(() => RecommendationScore.FromRating(6));
    }

    [Fact]
    public void PromptBuilderIncludesCoachMemoryAndHealthTrendDetails()
    {
        var prompt = new PromptBuilder().Build(
            new AIContextDocument(
                null,
                null,
                "{}",
                3,
                CoachMemoryContext: "{\"previousWorkoutGoals\":[\"running\"]}",
                CoachTrends: [new MetricTrendView("heart_rate", -3, "bpm")]),
            [],
            "¿Qué cambios han funcionado mejor?");

        Assert.Contains(prompt, item => item.Content.Contains("Coach memory", StringComparison.Ordinal));
        Assert.Contains(prompt, item => item.Content.Contains("heart_rate", StringComparison.Ordinal));
        Assert.Contains("Avoid asserting causation", prompt[0].Content);
    }

    [Fact]
    public async Task WorkoutGenerationUsesSuccessfulRecommendationOutcomesAndAdherence()
    {
        var planDays = Enumerable.Range(1, 3)
            .Select(offset => WorkoutDay.Create(
                DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-offset),
                $"Session {offset}",
                "",
                []))
            .ToArray();
        var previousPlan = WorkoutPlan.Create(
            "user-1",
            "general_health",
            planDays[^1].Date,
            planDays[0].Date,
            planDays,
            Now.AddDays(-4));
        var history = planDays.Select(day => new WorkoutHistory(
            Guid.NewGuid(),
            previousPlan.Id,
            day.Id,
            previousPlan.Goal,
            day.Date,
            day.Title,
            true,
            "Appropriate",
            Now)).ToArray();
        var repository = new TestWorkoutRepository(previousPlan, history);
        var recommendation = RecommendationHistory.Create(
            "user-1",
            previousPlan.Id,
            "training",
            "Mantén una progresión gradual.",
            Now.AddDays(-2));
        var anotherRecommendation = RecommendationHistory.Create(
            "user-1",
            previousPlan.Id,
            "training",
            "Mantén una progresión gradual.",
            Now.AddDays(-1));
        var outcomes = new[]
        {
            RecommendationOutcome.Create("user-1", recommendation.Id, true, "Helpful", 0.9, Now),
            RecommendationOutcome.Create("user-1", anotherRecommendation.Id, true, "Helpful", 0.9, Now)
        };
        var insightsRepository = new TestInsightsRepository([recommendation, anotherRecommendation], outcomes);
        var client = new CapturingOpenAIClient(
            """{"goal":"general_health","days":[{"title":"Sesión","recoveryNote":"","exercises":[{"name":"Sentadilla","sets":"3","repetitions":"8","durationMinutes":20,"restSeconds":60,"instructions":null}]}]}""");
        var service = new WorkoutService(
            new TestContextSource(),
            client,
            repository,
            new FixedTimeProvider(Now),
            insightsRepository);

        var plan = await service.GeneratePlanAsync("user-1", "general_health", 1, CancellationToken.None);

        Assert.Equal("3", Assert.Single(plan.Days[0].Exercises).Sets);
        Assert.Contains("adherence", client.LastPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recommendationEffectiveness", client.LastPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddMetric(
        ICollection<AIHealthMetricContext> metrics,
        string type,
        double value,
        DateTimeOffset observedAt) =>
        metrics.Add(new AIHealthMetricContext(type, value, "unit", observedAt));

    private sealed class TestContextSource : IAIContextSource
    {
        public Task<AIUserContext> GetContextAsync(
            string userId,
            DateTimeOffset from,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AIUserContext("Ada", 34, 170, "general_health", [
                new AIHealthMetricContext("workout_duration", 1800, "s", Now.AddDays(-1))
            ]));
    }

    private sealed class CapturingOpenAIClient(string content) : IOpenAIClient
    {
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<AIChatCompletion> CompleteAsync(
            IReadOnlyList<AIChatPromptMessage> messages,
            CancellationToken cancellationToken)
        {
            LastPrompt = string.Join("\n", messages.Select(message => message.Content));
            return Task.FromResult(new AIChatCompletion(content, "test-model", 100, 50));
        }
    }

    private sealed class TestWorkoutRepository(
        WorkoutPlan initialPlan,
        IReadOnlyList<WorkoutHistory> initialHistory) : IWorkoutRepository
    {
        private readonly List<WorkoutPlan> plans = [initialPlan];

        public Task SavePlanAsync(WorkoutPlan plan, CancellationToken cancellationToken)
        {
            plans.Add(plan);
            return Task.CompletedTask;
        }

        public Task<WorkoutPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(plans.Where(item => item.UserId == userId)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefault());

        public Task<IReadOnlyList<WorkoutPlan>> GetPlansAsync(
            string userId,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutPlan>>(plans.Where(item => item.UserId == userId).Take(count).ToArray());

        public Task<WorkoutPlan?> GetOwnedPlanAsync(string userId, Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(plans.SingleOrDefault(item => item.UserId == userId && item.Id == planId));

        public Task<IReadOnlyList<WorkoutHistory>> GetHistoryAsync(
            string userId,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutHistory>>(initialHistory.Take(count).ToArray());

        public Task<WorkoutPlan?> GetOwnedPlanByDayAsync(
            string userId,
            Guid workoutDayId,
            CancellationToken cancellationToken) =>
            Task.FromResult(plans.FirstOrDefault(plan =>
                plan.UserId == userId && plan.Days.Any(day => day.Id == workoutDayId)));

        public Task AddFeedbackAsync(
            WorkoutFeedback feedback,
            WorkoutCompletion completion,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddCompletionAsync(WorkoutCompletion completion, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<WorkoutFeedback>> GetRecentFeedbackAsync(
            string userId,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutFeedback>>([]);

        public Task<IReadOnlyList<WorkoutRecommendation>> GetRecommendationsAsync(
            string userId,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutRecommendation>>([]);

        public Task AddRecommendationAsync(
            WorkoutRecommendation recommendation,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestInsightsRepository(
        IReadOnlyList<RecommendationHistory> recommendations,
        IReadOnlyList<RecommendationOutcome> outcomes) : ICoachInsightsRepository
    {
        public Task SaveAnalysisAsync(
            UserInsights insights,
            IReadOnlyList<BehaviorPattern> patterns,
            TrainingAdherence adherence,
            IReadOnlyList<GoalAchievement> achievements,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddRecommendationAsync(
            RecommendationHistory item,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddOutcomeAsync(RecommendationOutcome outcome, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AIHealthMetricContext>> GetTrendMetricsAsync(
            string userId,
            DateTimeOffset from,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AIHealthMetricContext>>([]);

        public Task<RecommendationHistory?> GetRecommendationAsync(
            string userId,
            Guid recommendationId,
            CancellationToken cancellationToken)
        {
            var recommendation = recommendations.SingleOrDefault(item =>
                item.UserId == userId && item.Id == recommendationId);
            return Task.FromResult(recommendation);
        }

        public Task<IReadOnlyList<BehaviorPattern>> GetPatternsAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BehaviorPattern>>([]);

        public Task<TrainingAdherence?> GetLatestAdherenceAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<TrainingAdherence?>(null);

        public Task<IReadOnlyList<GoalAchievement>> GetAchievementsAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoalAchievement>>([]);

        public Task<IReadOnlyList<RecommendationHistory>> GetRecommendationsAsync(
            string userId,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecommendationHistory>>(recommendations);

        public Task<IReadOnlyList<RecommendationOutcome>> GetOutcomesAsync(
            string userId,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcomes);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
