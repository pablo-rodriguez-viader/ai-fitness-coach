using System.Net;
using System.Text;
using System.Text.Json;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.AI.Services;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using AIFitnessCoach.Infrastructure.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class AIPlatformTests
{
    [Fact]
    public void PromptBuilderKeepsSystemHealthUserGoalAndConversationSeparate()
    {
        var builder = new PromptBuilder();
        var prompt = builder.Build(
            new AIContextDocument(
                "{\"displayName\":\"Ada\"}",
                "{\"objective\":\"endurance\"}",
                "{\"stepsLast7Days\":8000}",
                1),
            [Message.Create(Guid.NewGuid(), MessageRole.User, "previous question", DateTimeOffset.UtcNow)],
            "current question");

        Assert.Equal("system", prompt[0].Role);
        Assert.Contains("Health Context", prompt[1].Content);
        Assert.Contains("User Context", prompt[2].Content);
        Assert.Contains("Goal Context", prompt[3].Content);
        Assert.Equal("previous question", prompt[4].Content);
        Assert.Equal("current question", prompt[5].Content);
        Assert.Contains("never create a complete workout routine", prompt[0].Content);
        Assert.Contains("Do not diagnose disease", prompt[0].Content);
    }

    [Fact]
    public void PromptBuilderBoundsConversationHistoryWhileKeepingMostRecentMessages()
    {
        var conversationId = Guid.NewGuid();
        var history = new[]
        {
            Message.Create(conversationId, MessageRole.User, "old", DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            Message.Create(conversationId, MessageRole.Assistant, "prev", DateTimeOffset.Parse("2026-01-02T00:00:00Z")),
            Message.Create(conversationId, MessageRole.User, "last", DateTimeOffset.Parse("2026-01-03T00:00:00Z"))
        };
        var prompt = new PromptBuilder().Build(
            new AIContextDocument(null, null, "{}", 1),
            history,
            "now",
            maxHistoryCharacters: 5);
        var historyMessages = prompt.Skip(2).Take(2).ToArray();

        Assert.Equal(5, historyMessages.Sum(message => message.Content.Length));
        Assert.Equal("last", historyMessages[^1].Content);
    }

    [Fact]
    public void AIOptionsRequireCredentialsAndPricingOnlyWhenEnabled()
    {
        Assert.True(new AIOptions().IsValid(out _));
        Assert.False(new AIOptions { Enabled = true }.IsValid(out _));
        Assert.True(new AIOptions
        {
            Enabled = true,
            ApiKey = "key",
            Model = "test-model",
            InputCostPerMillionTokens = 1,
            OutputCostPerMillionTokens = 1
        }.IsValid(out _));
    }

    [Fact]
    public async Task ContextBuilderIncludesProfileGoalHealthMetricsAndRecentProgress()
    {
        var now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
        var source = new FakeAIContextSource(new AIUserContext(
            "Ada",
            34,
            170,
            "cardio",
            [
                Metric("steps", 8_000, "count", now.AddDays(-1)),
                Metric("body_mass", 70, "kg", now.AddDays(-10)),
                Metric("body_mass", 69.5, "kg", now.AddDays(-1)),
                Metric("sleep_duration", 28_800, "s", now.AddDays(-1)),
                Metric("heart_rate", 62, "count/min", now.AddDays(-1)),
                Metric("workout_duration", 3_600, "s", now.AddDays(-2))
            ]));
        var builder = new AIContextBuilder(
            source,
            new HealthSummaryAnalyzer(),
            new HealthTrendAnalyzer(),
            new ProgressAnalyzer(),
            new FixedTimeProvider(now));

        var context = await builder.BuildAsync("user-1", 90, CancellationToken.None);
        using var health = JsonDocument.Parse(context.HealthContext);

        Assert.Equal(3, context.Version);
        // Data minimisation: age and height are sent, the name never is.
        Assert.DoesNotContain("Ada", context.UserContext);
        Assert.Contains("34", context.UserContext);
        Assert.Contains("cardio", context.GoalContext);
        Assert.Equal(8_000, health.RootElement.GetProperty("stepsLast7Days").GetDouble());
        Assert.Equal(8, health.RootElement.GetProperty("averageSleepHoursLast7Days").GetDouble());
        Assert.Equal(69.5, health.RootElement.GetProperty("latestWeightKilograms").GetDouble());
        Assert.Equal(1, health.RootElement.GetProperty("workoutsLast7Days").GetInt32());
        Assert.Equal(1, context.WeeklySummary!.DaysWithSteps);
        Assert.Equal("low", context.Progress!.ConsistencyLabel);
        Assert.Equal(-0.5, context.Trends!.WeightChangeKilograms);
    }

    [Fact]
    public async Task AIContextSourceKeepsMetricTypesRepresentedWhenOneMetricHasManySamples()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 120; index++)
        {
            dbContext.HealthMetrics.Add(HealthMetric.Create(
                Guid.NewGuid(),
                "user-1",
                "heart_rate",
                60 + index % 10,
                "count/min",
                now.AddMinutes(-index),
                now,
                "apple_health",
                $"hr-{index}",
                "device-1"));
        }
        dbContext.HealthMetrics.Add(HealthMetric.Create(
            Guid.NewGuid(),
            "user-1",
            "steps",
            8_000,
            "count",
            now.AddHours(-1),
            now,
            "apple_health",
            "steps-today",
            "device-1"));
        await dbContext.SaveChangesAsync();

        var context = await new AIContextSource(dbContext).GetContextAsync(
            "user-1",
            now.AddDays(-30),
            CancellationToken.None);

        Assert.Equal(30, context.RecentMetrics.Count(metric => metric.Metric == "heart_rate"));
        Assert.Contains(context.RecentMetrics, metric => metric.Metric == "steps");
    }

    [Fact]
    public void TokenUsageServiceEstimatesInputAndOutputCostFromProviderUsage()
    {
        var service = new TokenUsageService(
            new FakeAIConversationRepository(),
            Options.Create(new AIOptions
            {
                InputCostPerMillionTokens = 2m,
                OutputCostPerMillionTokens = 8m
            }),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var cost = service.EstimateCost(new AIChatCompletion("ok", "test-model", 1_000, 500));

        Assert.Equal(0.006m, cost);
    }

    [Fact]
    public void ConversationAccumulatesTokenUsageAndRejectsNegativeValues()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("user-1", now);
        conversation.RecordUsage(11, 7, 0.0001m, now.AddMinutes(1));
        conversation.RecordUsage(5, 4, 0.0002m, now.AddMinutes(2));

        Assert.Equal(16, conversation.InputTokens);
        Assert.Equal(11, conversation.OutputTokens);
        Assert.Equal(0.0003m, conversation.EstimatedCost);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => conversation.RecordUsage(-1, 0, 0, now));
    }

    [Fact]
    public void WeeklyMonthlyAndProgressAnalyzersSummarizeObservedMetrics()
    {
        var now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
        var metrics = new[]
        {
            Metric("steps", 7_000, "count", now.AddDays(-1)),
            Metric("steps", 6_000, "count", now.AddDays(-3)),
            Metric("steps", 4_000, "count", now.AddDays(-9)),
            Metric("sleep_duration", 25_200, "s", now.AddDays(-1)),
            Metric("sleep_duration", 28_800, "s", now.AddDays(-9)),
            Metric("body_mass", 71, "kg", now.AddDays(-20)),
            Metric("body_mass", 70, "kg", now.AddDays(-1)),
            Metric("workout_duration", 1_800, "s", now.AddDays(-2)),
            Metric("workout_duration", 2_400, "s", now.AddDays(-10)),
            Metric("resting_heart_rate", 60, "count/min", now.AddDays(-1))
        };
        var analyzer = new HealthSummaryAnalyzer();
        var currentWeek = analyzer.BuildWeekly(metrics, now);
        var previousWeek = analyzer.BuildWeekly(metrics, now, daysOffset: 7);
        var month = analyzer.BuildMonthly(metrics, now);
        var trends = new HealthTrendAnalyzer().Analyze(metrics, now);
        var progress = new ProgressAnalyzer().Analyze(currentWeek, previousWeek, "running");

        Assert.Equal(13_000, currentWeek.Steps);
        Assert.Equal(7, currentWeek.AverageSleepHours);
        Assert.Equal(1, currentWeek.Workouts);
        Assert.Equal(2, month.Workouts);
        Assert.Equal(-1, trends.WeightChangeKilograms);
        Assert.Equal(0, trends.WorkoutCountChange);
        Assert.Equal("building", progress.ConsistencyLabel);
        Assert.Contains("cardiovascular", progress.GoalAlignment);
    }

    [Fact]
    public async Task OpenAIClientSendsConfiguredModelAndParsesUsage()
    {
        var handler = new OpenAIHandler();
        var client = new OpenAIClient(
            new HttpClient(handler),
            Options.Create(new AIOptions
            {
                Enabled = true,
                ApiKey = "test-secret",
                Model = "gpt-4o-mini",
                Endpoint = "https://api.openai.com/v1/"
            }));

        var result = await client.CompleteAsync(
            [new AIChatPromptMessage("user", "hello")],
            CancellationToken.None);

        Assert.Equal("test-model", result.Model);
        Assert.Equal(17, result.InputTokens);
        Assert.Equal(8, result.OutputTokens);
        Assert.Equal("Bearer test-secret", handler.Authorization);
        Assert.Equal("hello", handler.RequestBody!.RootElement
            .GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task AIServiceStoresUserAssistantContextAndTokenUsageForOwner()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        var repository = new AIConversationRepository(dbContext);
        var source = new AIContextSource(dbContext);
        var fakeOpenAI = new FakeOpenAIClient();
        var now = DateTimeOffset.UtcNow;
        var settings = Options.Create(new AIOptions
        {
            Enabled = true,
            Model = "test-model",
            ContextLookbackDays = 90,
            ConversationHistoryMessages = 12,
            InputCostPerMillionTokens = 2m,
            OutputCostPerMillionTokens = 8m
        });
        var aiService = new AIService(
            new AIContextBuilder(
                source,
                new HealthSummaryAnalyzer(),
                new HealthTrendAnalyzer(),
                new ProgressAnalyzer(),
                new FixedTimeProvider(now)),
            new PromptBuilder(),
            new ConversationService(repository, new FixedTimeProvider(now)),
            repository,
            fakeOpenAI,
            new TokenUsageService(repository, settings, new FixedTimeProvider(now)),
            settings,
            new FixedTimeProvider(now));
        var coach = new CoachService(aiService);

        var response = await coach.AskAsync(
            "user-1",
            new CoachCommand(null, "How was my recent activity?"),
            CancellationToken.None);

        Assert.Equal("assistant", response.Answer.Role);
        Assert.Equal(23, response.InputTokens);
        Assert.Equal(9, response.OutputTokens);
        Assert.Equal(1, await dbContext.AIConversations.CountAsync());
        Assert.Equal(2, await dbContext.AIMessages.CountAsync());
        Assert.Equal(1, await dbContext.AIContextSnapshots.CountAsync());
        Assert.Equal(1, await dbContext.AITokenUsage.CountAsync());

        var saved = await repository.GetOwnedAsync("user-1", response.ConversationId, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(23, saved.InputTokens);
        Assert.Equal(9, saved.OutputTokens);
        Assert.Null(await repository.GetOwnedAsync("user-2", response.ConversationId, CancellationToken.None));
    }

    [Fact]
    public async Task ConversationRepositoryScopesReadsAndDeletesToOwner()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
        dbContext.Users.Add(User.Create("user-2", "Grace", DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        var repository = new AIConversationRepository(dbContext);
        var conversation = await repository.CreateAsync("user-1", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Null(await repository.GetOwnedAsync("user-2", conversation.Id, CancellationToken.None));
        Assert.False(await repository.DeleteOwnedAsync("user-2", conversation.Id, CancellationToken.None));
        Assert.True(await repository.DeleteOwnedAsync("user-1", conversation.Id, CancellationToken.None));
        Assert.Empty(await repository.ListOwnedAsync("user-1", 50, CancellationToken.None));
    }

    [Fact]
    public void AIEntitiesMapToExpectedTablesAndOwnershipIndexes()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new FitnessDbContext(options);

        var conversation = dbContext.Model.FindEntityType(typeof(Conversation))
            ?? throw new InvalidOperationException("AI conversation mapping is missing.");
        var message = dbContext.Model.FindEntityType(typeof(Message))
            ?? throw new InvalidOperationException("AI message mapping is missing.");
        var usage = dbContext.Model.FindEntityType(typeof(AITokenUsage))
            ?? throw new InvalidOperationException("AI token usage mapping is missing.");
        var snapshot = dbContext.Model.FindEntityType(typeof(AIContextSnapshot))
            ?? throw new InvalidOperationException("AI context snapshot mapping is missing.");

        Assert.Equal("ai_conversations", conversation.GetTableName());
        Assert.Equal("ai_messages", message.GetTableName());
        Assert.Equal("ai_token_usage", usage.GetTableName());
        Assert.Equal("ai_context_snapshots", snapshot.GetTableName());
        Assert.Contains(conversation.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["UserId", "UpdatedAt"]));
        Assert.Contains(usage.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["UserId", "CreatedAt"]));
        Assert.Contains(snapshot.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Any(property => property.Name == "UserId"));
    }

    private static AIHealthMetricContext Metric(
        string metric,
        double value,
        string unit,
        DateTimeOffset observedAt) =>
        new(metric, value, unit, observedAt);

    private sealed class FakeAIContextSource(AIUserContext context) : IAIContextSource
    {
        public Task<AIUserContext> GetContextAsync(
            string userId,
            DateTimeOffset from,
            CancellationToken cancellationToken) =>
            Task.FromResult(context);
    }

    private sealed class FakeAIConversationRepository : IAIConversationRepository
    {
        public Task<Conversation> CreateAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<Conversation?> GetOwnedAsync(string userId, Guid conversationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<int> CountOwnedSinceAsync(
            string userId,
            DateTimeOffset activeSince,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ConversationSummary>> ListOwnedAsync(
            string userId,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<bool> DeleteOwnedAsync(string userId, Guid conversationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AIUsageSummary> GetUsageSummaryAsync(
            string userId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AIUsageSummary(0, 0, 0, null));

        public Task AddUserMessageAsync(
            string userId,
            Guid conversationId,
            string content,
            AIContextSnapshot snapshot,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<Message> AddAssistantMessageAsync(
            string userId,
            Guid conversationId,
            string content,
            AIChatCompletion completion,
            decimal estimatedCost,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class FakeOpenAIClient : IOpenAIClient
    {
        public Task<AIChatCompletion> CompleteAsync(
            IReadOnlyList<AIChatPromptMessage> messages,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AIChatCompletion("Your recent activity is available.", "test-model", 23, 9));
    }

    private sealed class OpenAIHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public JsonDocument? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"test-model","choices":[{"message":{"content":"hello back"}}],"usage":{"prompt_tokens":17,"completion_tokens":8}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
