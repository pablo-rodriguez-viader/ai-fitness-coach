using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIFitnessCoach.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAIPlatform(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AIOptions>()
            .Bind(configuration.GetSection(AIOptions.SectionName))
            .Validate(options => options.IsValid(out _), "AI configuration is invalid.")
            .ValidateOnStart();
        services.PostConfigure<AIOptions>(options =>
            options.ApiKey = Environment.GetEnvironmentVariable("AI__ApiKey") ?? string.Empty);
        services.AddScoped<AIContextBuilder>();
        services.AddScoped<HealthSummaryAnalyzer>();
        services.AddScoped<HealthTrendAnalyzer>();
        services.AddScoped<ProgressAnalyzer>();
        services.AddScoped<TrendAnalyzer>();
        services.AddScoped<AdherenceAnalyzer>();
        services.AddScoped<BehaviorAnalyzer>();
        services.AddScoped<GoalAchievementAnalyzer>();
        services.AddScoped<CoachInsightsService>();
        services.AddSingleton<ExerciseCatalog>();
        services.AddScoped<WorkoutRuleEngine>();
        services.AddScoped<RecoveryEngine>();
        services.AddScoped<WorkoutPlanner>();
        services.AddScoped<WorkoutSafetyValidator>();
        services.AddScoped<ProgressionEngine>();
        services.AddScoped<NutritionAnalyzer>();
        services.AddScoped<MetabolicAnalyzer>();
        services.AddScoped<NutritionProgressAnalyzer>();
        services.AddScoped<GoalAnalyzer>();
        services.AddScoped<WeightTrendAnalyzer>();
        services.AddScoped<NutritionService>();
        services.AddScoped<PromptBuilder>();
        services.AddScoped<ConversationService>();
        services.AddScoped<TokenUsageService>();
        services.AddScoped<AIService>();
        services.AddScoped<CoachService>();
        services.AddScoped<WorkoutService>();
        services.AddScoped<AIFitnessCoach.AI.UseCases.GenerateWorkoutPlanUseCase>();
        services.AddScoped<AIFitnessCoach.AI.UseCases.GenerateDailyWorkoutUseCase>();
        services.AddScoped<AIFitnessCoach.AI.UseCases.AdaptWorkoutPlanUseCase>();
        return services;
    }
}
