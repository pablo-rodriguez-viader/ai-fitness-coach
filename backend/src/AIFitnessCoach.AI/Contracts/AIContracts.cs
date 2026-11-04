using AIFitnessCoach.AI.Models;

namespace AIFitnessCoach.AI.Contracts;

public sealed record ChatCommand(Guid? ConversationId, string Content);

public sealed record ChatResponse(
    Guid ConversationId,
    MessageView Message,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost);

public sealed record AIChatPromptMessage(string Role, string Content);

public sealed record AIChatCompletion(
    string Content,
    string Model,
    int InputTokens,
    int OutputTokens);

public sealed record AIHealthMetricContext(
    string Metric,
    double Value,
    string Unit,
    DateTimeOffset ObservedAt);

public sealed record AIUserContext(
    string? DisplayName,
    int? Age,
    double? HeightCentimeters,
    string? Goal,
    IReadOnlyList<AIHealthMetricContext> RecentMetrics,
    AIUserNutritionContext? Nutrition = null);

public sealed record AIContextDocument(
    string? UserContext,
    string? GoalContext,
    string HealthContext,
    int Version,
    WeeklyHealthSummary? WeeklySummary = null,
    MonthlyHealthSummary? MonthlySummary = null,
    HealthTrendSummary? Trends = null,
    ProgressSummary? Progress = null,
    string? ConversationContext = null,
    AIUserNutritionContext? NutritionContext = null,
    string? CoachMemoryContext = null,
    IReadOnlyList<MetricTrendView>? CoachTrends = null);

public sealed record CoachCommand(Guid? ConversationId, string Question);

public sealed record CoachResponse(
    Guid ConversationId,
    MessageView Answer,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost);
