using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.Infrastructure.Authentication;
using AIFitnessCoach.Infrastructure.AI;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIFitnessCoach.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // Transient failures (failover, restarts, dropped connections) are retried instead of
        // surfacing as 500s. Explicit transactions must run inside the execution strategy.
        services.AddDbContext<FitnessDbContext>(options => options
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null))
            // Migrations are written by hand and their snapshot is built from the same model
            // configuration without provider conventions, so EF's "pending model changes" check
            // always fires and would block every migration at startup.
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
        services.AddHttpClient<OpenAIClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
        // Every AI call goes through the circuit breaker (one per process).
        services.AddSingleton<AICircuitBreaker>();
        services.AddScoped<AIFitnessCoach.AI.Abstractions.IOpenAIClient>(provider => new CircuitBreakingOpenAIClient(
            provider.GetRequiredService<OpenAIClient>(),
            provider.GetRequiredService<AICircuitBreaker>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIFitnessCoach.AI.Configuration.AIOptions>>()));
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.IActivityAnalysisGateway,
            ActivityAnalysisGateway>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.IActivityAnalysisContextBuilder,
            ActivityAnalysisContextBuilder>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.IActivityAnalysisConversationStore,
            ActivityAnalysisConversationStore>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.IActivityAnalysisQuota,
            ActivityAnalysisQuota>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.ICoachConsentStore, CoachConsentStore>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.ICoachIdempotencyStore, CoachIdempotencyStore>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.ICoachMemoryStore, CoachMemoryStore>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.AI.ICoachFeedbackStore, CoachFeedbackStore>();
        services.AddScoped<IHealthMetricRepository, HealthMetricRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IAIConversationRepository, AIConversationRepository>();
        services.AddScoped<IAIContextSource, AIContextSource>();
        services.AddScoped<IWorkoutRepository, WorkoutRepository>();
        services.AddScoped<ICoachInsightsRepository, CoachInsightsRepository>();
        services.AddScoped<IWellnessRepository, WellnessRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IProductAnalyticsRepository, ProductAnalyticsRepository>();
        services.AddScoped<IProductEventRecorder, ProductEventRecorder>();
        services.AddScoped<IAccountDataStore, AccountDataStore>();
        services.AddScoped<IDataRetentionStore, DataRetentionStore>();
        services.AddScoped<IUserConsentStore, UserConsentStore>();
        services.AddScoped<ISupportReportRepository, SupportReportRepository>();
        services.AddHttpClient<IAppleTokenRevoker, AppleTokenRevoker>(
            client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IAppleAuthenticationService, AppleAuthenticationService>();
        return services;
    }
}
