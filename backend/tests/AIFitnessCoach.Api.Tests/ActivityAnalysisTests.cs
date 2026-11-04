using System.Text.Json;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.Application.UseCases.AI;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.AI;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class ActivityAnalysisTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
    private const string ValidAnalysis =
        """{"summary":"Tu actividad fue constante.","observations":[{"category":"activity","trend":"improving","title":"Pasos","description":"Aumentaron los pasos agregados.","evidence":["Promedio semanal disponible"],"confidence":"medium"}],"suggestions":[],"missingData":[],"safetyNotice":"Este análisis no es consejo médico.","limitations":"No hay datos de todos los días."}""";

    [Fact]
    public async Task UseCaseBuildsStructuredAnalysisAndRecordsTokenCost()
    {
        var gateway = new FakeGateway(ValidAnalysis);
        var store = new FakeConversationStore();
        var quota = new FakeQuota(true);
        var useCase = CreateUseCase(gateway, store, quota);

        var response = await useCase.ExecuteAsync(
            "user-1",
            new ActivityAnalysisRequest(null, "Resume mi actividad semanal."),
            CancellationToken.None);

        Assert.Equal(store.CreatedConversationId, response.ConversationId);
        Assert.Equal("Tu actividad fue constante.", response.Analysis.Summary);
        Assert.Equal("activity", Assert.Single(response.Analysis.Observations).Category);
        Assert.Equal(100, response.Usage.InputTokens);
        Assert.Equal(50, response.Usage.OutputTokens);
        Assert.Single(store.UserMessages);
        Assert.Single(store.AssistantMessages);
        Assert.Equal(0.0004m, store.AssistantMessages[0].EstimatedCost);
        Assert.Equal(1, quota.TokenReservations);
        Assert.Equal(ActivityAnalysisPromptBuilder.SystemPrompt, gateway.SystemPrompt);
        Assert.Contains("currentWeek", gateway.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("aggregated profile", gateway.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseCaseRejectsProviderOutputOutsideStructuredContract()
    {
        var useCase = CreateUseCase(
            new FakeGateway("""{"summary":"diagnosis","observations":[{"category":"medical","trend":"stable","description":"x"}],"limitations":""}"""),
            new FakeConversationStore(),
            new FakeQuota(true));

        await Assert.ThrowsAsync<ActivityAnalysisProviderException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new ActivityAnalysisRequest(null, "Analyze."),
                CancellationToken.None));
    }

    [Fact]
    public async Task UseCaseRejectsQuestionOverLimitBeforeReservingQuota()
    {
        var quota = new FakeQuota(true);
        var useCase = CreateUseCase(new FakeGateway(ValidAnalysis), new FakeConversationStore(), quota);

        await Assert.ThrowsAsync<AIFitnessCoach.Application.Errors.UseCaseValidationException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new ActivityAnalysisRequest(null, new string('a', 1_001)),
                CancellationToken.None));

        Assert.Equal(0, quota.Reservations);
    }

    [Fact]
    public async Task UseCaseRejectsUnsupportedPeriodBeforeReservingQuota()
    {
        var quota = new FakeQuota(true);
        var useCase = CreateUseCase(new FakeGateway(ValidAnalysis), new FakeConversationStore(), quota);

        await Assert.ThrowsAsync<AIFitnessCoach.Application.Errors.UseCaseValidationException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new ActivityAnalysisRequest(null, "Analyze", "last90Days"),
                CancellationToken.None));

        Assert.Equal(0, quota.Reservations);
    }

    [Fact]
    public async Task ThirtyDayPeriodIsReturnedAndStructuredSuggestionsAreValidated()
    {
        const string richJson = """{"summary":"Actividad estable.","observations":[{"category":"activity","trend":"stable","title":"Pasos","description":"Se mantuvieron estables.","evidence":["Promedio 7.000 pasos"],"confidence":"medium"}],"suggestions":[{"title":"Mantener ritmo","description":"Continúa con una rutina sostenible.","priority":"low"}],"missingData":["Sueño incompleto"],"safetyNotice":"No es consejo médico.","limitations":"Datos agregados."}""";
        var useCase = CreateUseCase(new FakeGateway(richJson), new FakeConversationStore(), new FakeQuota(true));

        var response = await useCase.ExecuteAsync(
            "user-1",
            new ActivityAnalysisRequest(null, "Resume el mes", "last30Days"),
            CancellationToken.None);

        Assert.Equal(Now.AddDays(-30), response.ContextPeriodStart);
        Assert.Equal(Now, response.ContextPeriodEnd);
        Assert.Equal("medium", Assert.Single(response.Analysis.Observations).Confidence);
        Assert.Equal("low", Assert.Single(response.Analysis.Suggestions!).Priority);
        Assert.Contains("Sueño incompleto", response.Analysis.MissingData!);
    }

    [Fact]
    public async Task UseCaseDoesNotCallProviderWhenDailyQuotaIsReached()
    {
        var gateway = new FakeGateway(ValidAnalysis);
        var useCase = CreateUseCase(gateway, new FakeConversationStore(), new FakeQuota(false));

        await Assert.ThrowsAsync<ActivityAnalysisQuotaExceededException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new ActivityAnalysisRequest(null, "Analyze."),
                CancellationToken.None));

        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task UseCaseRequiresConversationToBelongToRequestingUser()
    {
        var store = new FakeConversationStore();
        var useCase = CreateUseCase(new FakeGateway(ValidAnalysis), store, new FakeQuota(true));
        store.ExistingConversation = null;

        await Assert.ThrowsAsync<ActivityAnalysisConversationNotFoundException>(() =>
            useCase.ExecuteAsync(
                "user-1",
                new ActivityAnalysisRequest(Guid.NewGuid(), "Analyze."),
                CancellationToken.None));

        Assert.Equal(0, store.CreatedConversations);
    }

    [Fact]
    public async Task ConversationStorePersistsQuestionContextAnswerAndTokenUsage()
    {
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new FitnessDbContext(options);
        dbContext.Users.Add(User.Create("user-1", "Ada", Now));
        await dbContext.SaveChangesAsync();
        var store = new ActivityAnalysisConversationStore(
            new AIConversationRepository(dbContext),
            new FixedTimeProvider(Now));
        var conversationId = await store.CreateAsync("user-1", CancellationToken.None);

        await store.SaveUserMessageAsync(
            "user-1",
            conversationId,
            "¿Cómo fue mi semana?",
            """{"steps":12000,"rawSamples":false}""",
            CancellationToken.None);
        await store.SaveAssistantMessageAsync(
            "user-1",
            conversationId,
            ValidAnalysis,
            "test-model",
            100,
            50,
            0.0004m,
            CancellationToken.None);

        var saved = await store.GetOwnedAsync("user-1", conversationId, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(2, saved.Messages.Count);
        Assert.Equal(4, await dbContext.AIContextSnapshots.Select(item => item.ContextVersion).SingleAsync());
        var usage = await dbContext.AITokenUsage.SingleAsync();
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(50, usage.OutputTokens);
        Assert.Equal(0.0004m, usage.EstimatedCost);
        Assert.Null(await store.GetOwnedAsync("other-user", conversationId, CancellationToken.None));
    }

    [Fact]
    public void ContextPromptContainsOnlyAggregatesAndNotUserProfileIdentifiers()
    {
        var context = new ActivityAnalysisContext(
            new ActivityAnalysisProfile(34),
            "endurance",
            new ActivityPeriodSummary(Now.AddDays(-7), Now, 50_000, 7.2, 70, 3, 90, 62, 6, 3),
            new ActivityPeriodSummary(Now.AddDays(-14), Now.AddDays(-7), 40_000, 6.8, 70.5, 2, 60, 64, 5, 2),
            new ActivityPeriodSummary(Now.AddDays(-30), Now, 200_000, 7, 70, 10, 300, 63, 20, 10),
            [new ActivityMetricTrend("weight", -0.5, "kg")],
            "high",
            6,
            5,
            [new ActivityWorkoutSummary(DateOnly.FromDateTime(Now.UtcDateTime), "running", true, "Appropriate")]);

        var prompt = new ActivityAnalysisPromptBuilder().BuildUserPrompt(context, "¿Cómo voy?");
        using var json = JsonDocument.Parse(prompt[(prompt.IndexOf('{')..)]);

        Assert.Equal(50_000, json.RootElement
            .GetProperty("currentWeek")
            .GetProperty("steps")
            .GetDouble());
        Assert.DoesNotContain("rawSamples", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static AnalyzeActivityUseCase CreateUseCase(
        FakeGateway gateway,
        FakeConversationStore store,
        FakeQuota quota) =>
        new(
            new FakeContextBuilder(),
            new ActivityAnalysisPromptBuilder(),
            gateway,
            store,
            quota,
            new FixedTimeProvider(Now),
            dailyRequestLimit: 5,
            inputCostPerMillionTokens: 2,
            outputCostPerMillionTokens: 4);

    private sealed class FakeContextBuilder : IActivityAnalysisContextBuilder
    {
        public Task<ActivityAnalysisContext> BuildAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(new ActivityAnalysisContext(
                new ActivityAnalysisProfile(34),
                "endurance",
                new ActivityPeriodSummary(Now.AddDays(-7), Now, 50_000, 7, 70, 3, 90, 62, 5, 3),
                new ActivityPeriodSummary(Now.AddDays(-14), Now.AddDays(-7), 45_000, 6.5, 70.5, 2, 60, 64, 4, 2),
                new ActivityPeriodSummary(Now.AddDays(-30), Now, 200_000, 7, 70, 10, 300, 63, 20, 10),
                [new ActivityMetricTrend("weight", -0.5, "kg")],
                "high",
                5,
                4,
                []));
    }

    private sealed class FakeGateway(string content) : IActivityAnalysisGateway
    {
        public int Calls { get; private set; }
        public string SystemPrompt { get; private set; } = string.Empty;
        public string UserPrompt { get; private set; } = string.Empty;

        public Task<ActivityAnalysisProviderResult> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken)
        {
            Calls++;
            SystemPrompt = systemPrompt;
            UserPrompt = userPrompt;
            return Task.FromResult(new ActivityAnalysisProviderResult(content, "test-model", 100, 50));
        }
    }

    private sealed class FakeConversationStore : IActivityAnalysisConversationStore
    {
        public Guid CreatedConversationId { get; } = Guid.NewGuid();
        public int CreatedConversations { get; private set; }
        public ActivityAnalysisConversation? ExistingConversation { get; set; } =
            new(Guid.NewGuid(), []);
        public List<(string Question, string Context)> UserMessages { get; } = [];
        public List<(string Content, decimal EstimatedCost)> AssistantMessages { get; } = [];

        public Task<bool> CanCreateConversationAsync(
            string userId,
            DateTimeOffset activeSince,
            int maximumActiveConversations,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<ActivityAnalysisConversation?> GetOwnedAsync(
            string userId,
            Guid conversationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExistingConversation?.Id == conversationId ? ExistingConversation : null);

        public Task<Guid> CreateAsync(string userId, CancellationToken cancellationToken)
        {
            CreatedConversations++;
            return Task.FromResult(CreatedConversationId);
        }

        public Task SaveUserMessageAsync(
            string userId,
            Guid conversationId,
            string question,
            string contextSnapshot,
            CancellationToken cancellationToken,
            int contextVersion = 4)
        {
            UserMessages.Add((question, contextSnapshot));
            return Task.CompletedTask;
        }

        public Task<Guid> SaveAssistantMessageAsync(
            string userId,
            Guid conversationId,
            string response,
            string model,
            int inputTokens,
            int outputTokens,
            decimal estimatedCost,
            CancellationToken cancellationToken)
        {
            AssistantMessages.Add((response, estimatedCost));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class FakeQuota(bool canReserve) : IActivityAnalysisQuota
    {
        public int Reservations { get; private set; }
        public int TokenReservations { get; private set; }

        public Task<bool> TryReserveAsync(
            string userId,
            DateOnly date,
            int dailyLimit,
            CancellationToken cancellationToken)
        {
            Reservations++;
            return Task.FromResult(canReserve);
        }

        public Task<bool> TryReserveTokensAsync(
            string userId,
            DateOnly date,
            int tokenReserve,
            int dailyTokenLimit,
            CancellationToken cancellationToken)
        {
            TokenReservations++;
            return Task.FromResult(true);
        }

        public Task SettleTokensAsync(
            string userId,
            DateOnly date,
            int reservedTokens,
            int actualTokens,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
