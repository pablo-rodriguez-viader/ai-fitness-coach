using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class WorkoutCoachTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");

    [Fact]
    public async Task GeneratePlanReducesVolumeWhenRecentSleepIsLow()
    {
        var source = new FakeContextSource([
            Metric("sleep_duration", 5 * 3600, Now.AddDays(-1))
        ]);
        var repository = new FakeWorkoutRepository();
        var service = CreateService(source, repository, PlanJson(3));

        var result = await service.GeneratePlanAsync("user-1", "general_health", 1, CancellationToken.None);

        var day = Assert.Single(result.Days);
        Assert.Equal("2", day.Exercises[0].Sets);
        Assert.Contains("sueño", day.RecoveryNote, StringComparison.OrdinalIgnoreCase);
        Assert.Single(repository.Plans);
        Assert.Single(repository.Recommendations);
        Assert.Equal("recovery", repository.Recommendations[0].Type);
    }

    [Fact]
    public async Task GeneratePlanReducesVolumeAfterSeveralInactiveDays()
    {
        var source = new FakeContextSource([
            Metric("workout_duration", 1800, Now.AddDays(-4))
        ]);
        var service = CreateService(source, new FakeWorkoutRepository(), PlanJson(3, "running"));

        var plan = await service.GeneratePlanAsync("user-1", "running", 1, CancellationToken.None);

        Assert.Equal("2", plan.Days[0].Exercises[0].Sets);
        Assert.Contains("retoma gradualmente", plan.Days[0].RecoveryNote);
    }

    [Fact]
    public async Task GeneratePlanGraduallyIncreasesDifficultyAfterPositiveProgressAndFeedback()
    {
        var source = new FakeContextSource([
            Metric("workout_duration", 1200, Now.AddDays(-10)),
            Metric("workout_duration", 1800, Now.AddDays(-1))
        ]);
        var repository = new FakeWorkoutRepository
        {
            FeedbackHistory =
            [
                Feedback(true, WorkoutDifficulty.Appropriate),
                Feedback(true, WorkoutDifficulty.Appropriate),
                Feedback(true, WorkoutDifficulty.VeryEasy)
            ]
        };
        var service = CreateService(source, repository, PlanJson(3, "muscle_gain"));

        var plan = await service.GeneratePlanAsync("user-1", "muscle_gain", 1, CancellationToken.None);

        Assert.Equal("3", plan.Days[0].Exercises[0].Sets);
        Assert.Contains("progresión", plan.Days[0].RecoveryNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FeedbackIsOwnedAndUpdatesAdaptationHistory()
    {
        var repository = new FakeWorkoutRepository();
        var service = CreateService(new FakeContextSource([]), repository, PlanJson(3));
        var day = WorkoutDay.Create(DateOnly.FromDateTime(Now.UtcDateTime), "Recovery", "", []);
        var plan = WorkoutPlan.Create("user-1", "general_health", day.Date, day.Date, [day], Now);
        repository.OwnedDay = plan;

        var response = await service.RecordFeedbackAsync(
            "user-1",
            new WorkoutFeedbackRequest(day.Id, false, "TooHard", "No pude completar"),
            CancellationToken.None);

        Assert.False(response.Completed);
        Assert.Contains("reducir", response.Adaptation);
        Assert.Single(repository.FeedbackItems);
        Assert.Single(repository.Completions);

        await Assert.ThrowsAsync<WorkoutNotFoundException>(() =>
            service.RecordFeedbackAsync(
                "other-user",
                new WorkoutFeedbackRequest(day.Id, true, "Appropriate", null),
                CancellationToken.None));
    }

    [Fact]
    public async Task NegativeFeedbackReducesTheNextPlanVolume()
    {
        var source = new FakeContextSource([Metric("workout_duration", 1800, Now.AddDays(-1))]);
        var repository = new FakeWorkoutRepository
        {
            FeedbackHistory = [Feedback(false, WorkoutDifficulty.VeryHard)]
        };
        var service = CreateService(source, repository, PlanJson(3));

        var plan = await service.GeneratePlanAsync("user-1", "general_health", 1, CancellationToken.None);

        Assert.Equal("2", plan.Days[0].Exercises[0].Sets);
        Assert.Contains("feedback", plan.Days[0].RecoveryNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepositoryPersistsPlanExercisesFeedbackAndCompletions()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        await dbContext.SaveChangesAsync();
        var repository = new WorkoutRepository(dbContext);
        var exercise = WorkoutExercise.Create(0, "Squat", "3", "8", null, 60, null);
        var day = WorkoutDay.Create(DateOnly.FromDateTime(Now.UtcDateTime), "Strength", "Recover well.", [exercise]);
        var plan = WorkoutPlan.Create("user-1", "muscle_gain", day.Date, day.Date, [day], Now);
        await repository.SavePlanAsync(plan, CancellationToken.None);

        var loaded = await repository.GetLatestPlanAsync("user-1", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("Squat", Assert.Single(Assert.Single(loaded.Days).Exercises).Name);

        await repository.AddFeedbackAsync(
            WorkoutFeedback.Create("user-1", day.Id, true, WorkoutDifficulty.Appropriate, null, Now),
            WorkoutCompletion.Create("user-1", day.Id, true, Now),
            CancellationToken.None);
        Assert.Single(await repository.GetRecentFeedbackAsync("user-1", 10, CancellationToken.None));
        Assert.Single(await dbContext.WorkoutCompletions.ToArrayAsync());
        Assert.Single(await repository.GetHistoryAsync("user-1", 10, CancellationToken.None));
    }

    [Fact]
    public async Task UnsupportedGoalAndPlanLengthAreRejected()
    {
        var service = CreateService(new FakeContextSource([]), new FakeWorkoutRepository(), PlanJson(3));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GeneratePlanAsync("user-1", "nutrition", 1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.GeneratePlanAsync("user-1", "running", 8, CancellationToken.None));
    }

    private static WorkoutService CreateService(
        FakeContextSource source,
        FakeWorkoutRepository repository,
        string planJson) =>
        new(source, new FakeOpenAIClient(planJson), repository, new FixedTimeProvider(Now));

    private static AIHealthMetricContext Metric(string type, double value, DateTimeOffset observedAt) =>
        new(type, value, type == "sleep_duration" ? "s" : "s", observedAt);

    private static WorkoutFeedback Feedback(bool completed, WorkoutDifficulty difficulty) =>
        WorkoutFeedback.Create(
            "user-1",
            Guid.NewGuid(),
            completed,
            difficulty,
            null,
            Now);

    private static string PlanJson(int sets, string goal = "general_health") =>
        $$"""{"goal":"{{goal}}","days":[{"title":"Sesión controlada","recoveryNote":"Descansa entre ejercicios.","exercises":[{"name":"Sentadillas","sets":"{{sets}}","repetitions":"8","durationMinutes":20,"restSeconds":60,"instructions":"Mantén un ritmo cómodo."}]}]}""";

    private sealed class FakeContextSource(IReadOnlyList<AIHealthMetricContext> metrics) : IAIContextSource
    {
        public Task<AIUserContext> GetContextAsync(
            string userId,
            DateTimeOffset from,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AIUserContext("Ada", 34, 170, "general_health", metrics));
    }

    private sealed class FakeOpenAIClient(string content) : IOpenAIClient
    {
        public Task<AIChatCompletion> CompleteAsync(
            IReadOnlyList<AIChatPromptMessage> messages,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AIChatCompletion(content, "test-model", 10, 10));
    }

    private sealed class FakeWorkoutRepository : IWorkoutRepository
    {
        public List<WorkoutPlan> Plans { get; } = [];
        public List<WorkoutFeedback> FeedbackItems { get; } = [];
        public List<WorkoutCompletion> Completions { get; } = [];
        public List<WorkoutRecommendation> Recommendations { get; } = [];
        public IReadOnlyList<WorkoutFeedback> FeedbackHistory { get; set; } = [];
        public WorkoutPlan? OwnedDay { get; set; }

        public Task SavePlanAsync(WorkoutPlan plan, CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            return Task.CompletedTask;
        }

        public Task<WorkoutPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(Plans.Where(plan => plan.UserId == userId).OrderByDescending(plan => plan.CreatedAt).FirstOrDefault());

        public Task<IReadOnlyList<WorkoutPlan>> GetPlansAsync(string userId, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutPlan>>(Plans.Where(plan => plan.UserId == userId).Take(count).ToArray());

        public Task<WorkoutPlan?> GetOwnedPlanAsync(string userId, Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plans.SingleOrDefault(plan => plan.UserId == userId && plan.Id == planId));

        public Task<IReadOnlyList<WorkoutHistory>> GetHistoryAsync(string userId, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutHistory>>([]);

        public Task<WorkoutPlan?> GetOwnedPlanByDayAsync(string userId, Guid workoutDayId, CancellationToken cancellationToken) =>
            Task.FromResult(OwnedDay?.UserId == userId && OwnedDay.Days.Any(day => day.Id == workoutDayId) ? OwnedDay : null);

        public Task AddFeedbackAsync(
            WorkoutFeedback feedback,
            WorkoutCompletion completion,
            CancellationToken cancellationToken)
        {
            FeedbackItems.Add(feedback);
            Completions.Add(completion);
            return Task.CompletedTask;
        }

        public Task AddCompletionAsync(WorkoutCompletion completion, CancellationToken cancellationToken)
        {
            Completions.Add(completion);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkoutFeedback>> GetRecentFeedbackAsync(string userId, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutFeedback>>(FeedbackHistory.Take(count).ToArray());

        public Task<IReadOnlyList<WorkoutRecommendation>> GetRecommendationsAsync(string userId, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutRecommendation>>(Recommendations.Take(count).ToArray());

        public Task AddRecommendationAsync(WorkoutRecommendation recommendation, CancellationToken cancellationToken)
        {
            Recommendations.Add(recommendation);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
