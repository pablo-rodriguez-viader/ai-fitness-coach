using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.AI;
using AIFitnessCoach.Infrastructure;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Application.UseCases.Profiles;
using AIFitnessCoach.Application.UseCases.Authentication;
using AIFitnessCoach.Application.UseCases.AI;
using AIFitnessCoach.AI.Configuration;

namespace AIFitnessCoach.Api;

public static class ApiServiceCollectionExtensions
{
    public static void AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOperationalServices(configuration);
        services.AddObservability(configuration);
        ReleaseConfiguration.Validate(configuration);
        services.AddScoped<AIFitnessCoach.Application.UseCases.Account.ExportAccountDataUseCase>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.Account.DeleteAccountUseCase>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.Account.SubmitSupportReportUseCase>();
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.ProductMetricsCalculator>();
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.RetentionAnalyzer>();
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.AdherenceEngine>();
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.DropoutRiskAnalyzer>();
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.ProductInsights>();
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.MotivationEngine>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.Analytics.GetEngagementSummaryUseCase>();
        services.AddScoped<AIFitnessCoach.Application.UseCases.Analytics.GetProductAnalyticsUseCase>();
        services.AddScoped<CreateHealthMetricUseCase>();
        services.AddScoped<GetHealthMetricsUseCase>();
        services.AddScoped<CreateUserProfileUseCase>();
        services.AddScoped<GetUserProfileUseCase>();
        services.AddScoped<SyncHealthMetricsUseCase>();
        services.AddScoped<GetLatestHealthMetricsUseCase>();
        services.AddScoped<GetHealthSummaryUseCase>();
        services.AddScoped<DeleteHealthMetricsUseCase>();
        services.AddScoped<ActivityAnalysisPromptBuilder>();
        services.AddScoped(provider => new CreateCoachConversationUseCase(
            provider.GetRequiredService<IActivityAnalysisConversationStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIOptions>>()
                .Value.MaximumActiveCoachConversations));
        services.AddScoped<CoachIntentClassifier>();
        services.AddScoped<CoachContextSelector>();
        services.AddScoped<CoachPromptBuilder>();
        // Beta:MaxUsers caps new accounts for a staged beta (10 → 25 → 50); 0 or unset = no cap.
        var maxBetaUsers = configuration.GetValue<int?>("Beta:MaxUsers") ?? 0;
        if (maxBetaUsers is < 0 or > 100_000)
            throw new InvalidOperationException("Beta:MaxUsers must be between 0 (no cap) and 100000.");
        var betaSettings = new AIFitnessCoach.Application.UseCases.Analytics.BetaSettings(
            maxBetaUsers == 0 ? null : maxBetaUsers);
        services.AddSingleton(betaSettings);
        services.AddSingleton<AIFitnessCoach.Application.UseCases.Analytics.BetaReadinessAnalyzer>();
        services.AddHostedService<BetaKpiPublisher>();
        // Validated here so a bad retention setting fails at startup, not at the first purge.
        DataRetentionService.ReadPolicy(configuration);
        services.AddHostedService<DataRetentionService>();
        // Personal install without Sign in with Apple: off unless a long secret code is set.
        var personalAccessCode = configuration["Auth:PersonalAccessCode"];
        if (string.IsNullOrWhiteSpace(personalAccessCode))
            personalAccessCode = null;
        else if (personalAccessCode.Length < AuthenticationService.MinimumPersonalAccessCodeLength)
            throw new InvalidOperationException(
                $"Auth:PersonalAccessCode must have at least {AuthenticationService.MinimumPersonalAccessCodeLength} characters.");
        services.AddScoped<AuthenticationService>(provider =>
        {
            var accessMinutes = configuration.GetValue<int?>("Jwt:AccessTokenMinutes") ?? 15;
            var refreshDays = configuration.GetValue<int?>("Jwt:RefreshTokenDays") ?? 30;
            if (accessMinutes is < 1 or > 1440 || refreshDays is < 1 or > 365)
                throw new InvalidOperationException("JWT access and refresh token lifetimes are invalid.");
            return new AuthenticationService(
                provider.GetRequiredService<AIFitnessCoach.Application.Abstractions.Persistence.IAuthRepository>(),
                provider.GetRequiredService<AIFitnessCoach.Application.Abstractions.Authentication.IAppleAuthenticationService>(),
                provider.GetRequiredService<AIFitnessCoach.Application.Abstractions.Authentication.ITokenService>(),
                provider.GetRequiredService<TimeProvider>(),
                accessMinutes,
                refreshDays,
                betaSettings.MaxUsers,
                personalAccessCode);
        });
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        services.AddInfrastructure(connectionString);
        services.AddAIPlatform(configuration);
        services.AddScoped(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIOptions>>().Value;
            return new AnalyzeActivityUseCase(
                provider.GetRequiredService<IActivityAnalysisContextBuilder>(),
                provider.GetRequiredService<ActivityAnalysisPromptBuilder>(),
                provider.GetRequiredService<IActivityAnalysisGateway>(),
                provider.GetRequiredService<IActivityAnalysisConversationStore>(),
                provider.GetRequiredService<IActivityAnalysisQuota>(),
                provider.GetRequiredService<TimeProvider>(),
                options.DailyAnalysisRequestLimit,
                options.InputCostPerMillionTokens,
                options.OutputCostPerMillionTokens,
                options.DailyCoachTokenLimit,
                options.MaxOutputTokens);
        });
        services.AddScoped(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIOptions>>().Value;
            return new SendCoachMessageUseCase(
                provider.GetRequiredService<ICoachConsentStore>(),
                provider.GetRequiredService<ICoachIdempotencyStore>(),
                provider.GetRequiredService<IActivityAnalysisContextBuilder>(),
                provider.GetRequiredService<CoachContextSelector>(),
                provider.GetRequiredService<CoachIntentClassifier>(),
                provider.GetRequiredService<CoachPromptBuilder>(),
                provider.GetRequiredService<IActivityAnalysisGateway>(),
                provider.GetRequiredService<IActivityAnalysisConversationStore>(),
                provider.GetRequiredService<ICoachMemoryStore>(),
                provider.GetRequiredService<IActivityAnalysisQuota>(),
                provider.GetRequiredService<TimeProvider>(),
                options.DailyAnalysisRequestLimit,
                options.DailyCoachTokenLimit,
                options.MaxOutputTokens,
                options.MaximumActiveCoachConversations,
                options.MaximumCoachConversationMessages,
                options.InputCostPerMillionTokens,
                options.OutputCostPerMillionTokens);
        });
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddApiAuthentication(configuration);
    }

    public static async Task ApplyDevelopmentMigrationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
