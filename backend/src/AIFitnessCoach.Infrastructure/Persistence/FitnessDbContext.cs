using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.AI.Models;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence;

public sealed class FitnessDbContext(DbContextOptions<FitnessDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<HealthMetric> HealthMetrics => Set<HealthMetric>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Conversation> AIConversations => Set<Conversation>();
    public DbSet<Message> AIMessages => Set<Message>();
    public DbSet<AITokenUsage> AITokenUsage => Set<AITokenUsage>();
    public DbSet<AIContextSnapshot> AIContextSnapshots => Set<AIContextSnapshot>();
    public DbSet<AIConsent> AIConsents => Set<AIConsent>();
    public DbSet<AICoachRequest> AICoachRequests => Set<AICoachRequest>();
    public DbSet<AIConversationSummaryRecord> AIConversationSummaries => Set<AIConversationSummaryRecord>();
    public DbSet<AIFeedback> AIFeedback => Set<AIFeedback>();
    public DbSet<WorkoutPlan> WorkoutPlans => Set<WorkoutPlan>();
    public DbSet<WorkoutDay> WorkoutDays => Set<WorkoutDay>();
    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();
    public DbSet<WorkoutCompletion> WorkoutCompletions => Set<WorkoutCompletion>();
    public DbSet<WorkoutFeedback> WorkoutFeedback => Set<WorkoutFeedback>();
    public DbSet<WorkoutRecommendation> WorkoutRecommendations => Set<WorkoutRecommendation>();
    public DbSet<UserInsights> UserInsights => Set<UserInsights>();
    public DbSet<BehaviorPattern> BehaviorPatterns => Set<BehaviorPattern>();
    public DbSet<TrainingAdherence> TrainingAdherence => Set<TrainingAdherence>();
    public DbSet<GoalAchievement> GoalAchievements => Set<GoalAchievement>();
    public DbSet<RecommendationHistory> RecommendationHistory => Set<RecommendationHistory>();
    public DbSet<RecommendationOutcome> RecommendationOutcomes => Set<RecommendationOutcome>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalProgress> GoalProgress => Set<GoalProgress>();
    public DbSet<NutritionProfile> NutritionProfiles => Set<NutritionProfile>();
    public DbSet<NutritionLog> NutritionLogs => Set<NutritionLog>();
    public DbSet<MacroTarget> MacroTargets => Set<MacroTarget>();
    public DbSet<DailyNutritionSummary> DailyNutritionSummaries => Set<DailyNutritionSummary>();
    public DbSet<ProductEvent> ProductEvents => Set<ProductEvent>();
    public DbSet<SupportReport> SupportReports => Set<SupportReport>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }
}
