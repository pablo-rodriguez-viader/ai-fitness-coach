using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.AI.Models;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence;

internal static class FitnessDbContextModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id).HasName("pk_users");
            entity.Property(user => user.Id).HasColumnName("id").HasMaxLength(128);
            entity.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(user => user.CreatedAt).HasDatabaseName("ix_users_created_at");
            entity.HasOne(user => user.Profile)
                .WithOne()
                .HasForeignKey<UserProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(user => user.HealthMetrics)
                .WithOne(metric => metric.User)
                .HasForeignKey(metric => metric.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(user => user.Identities)
                .WithOne(identity => identity.User)
                .HasForeignKey(identity => identity.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(user => user.Sessions)
                .WithOne(session => session.User)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserIdentity>(entity =>
        {
            entity.ToTable("user_identities", table =>
            {
                table.HasCheckConstraint("ck_user_identities_provider", "provider IN ('apple', 'personal')");
            });
            entity.HasKey(identity => identity.Id).HasName("pk_user_identities");
            entity.Property(identity => identity.Id).HasColumnName("id");
            entity.Property(identity => identity.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(identity => identity.Provider).HasColumnName("provider").HasMaxLength(32).IsRequired();
            entity.Property(identity => identity.ProviderSubject).HasColumnName("provider_subject").HasMaxLength(256).IsRequired();
            entity.Property(identity => identity.Email).HasColumnName("email").HasMaxLength(320);
            entity.Property(identity => identity.EmailVerified).HasColumnName("email_verified");
            entity.Property(identity => identity.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(identity => new { identity.Provider, identity.ProviderSubject })
                .IsUnique()
                .HasDatabaseName("ux_user_identities_provider_subject");
            entity.HasIndex(identity => identity.UserId).HasDatabaseName("ix_user_identities_user_id");
        });

        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.ToTable("auth_sessions", table =>
            {
                table.HasCheckConstraint("ck_auth_sessions_expiry", "expires_at > created_at");
            });
            entity.HasKey(session => session.Id).HasName("pk_auth_sessions");
            entity.Property(session => session.Id).HasColumnName("id");
            entity.Property(session => session.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(session => session.CreatedAt).HasColumnName("created_at");
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at");
            entity.Property(session => session.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(session => session.RevokedAt).HasColumnName("revoked_at");
            entity.HasIndex(session => new { session.UserId, session.RevokedAt, session.ExpiresAt })
                .HasDatabaseName("ix_auth_sessions_user_active");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens", table =>
            {
                table.HasCheckConstraint("ck_refresh_tokens_expiry", "expires_at > created_at");
            });
            entity.HasKey(token => token.Id).HasName("pk_refresh_tokens");
            entity.Property(token => token.Id).HasColumnName("id");
            entity.Property(token => token.AuthSessionId).HasColumnName("auth_session_id");
            entity.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(token => token.CreatedAt).HasColumnName("created_at");
            entity.Property(token => token.ExpiresAt).HasColumnName("expires_at");
            entity.Property(token => token.RevokedAt).HasColumnName("revoked_at");
            entity.Property(token => token.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash").HasMaxLength(64);
            entity.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
            entity.HasIndex(token => new { token.AuthSessionId, token.RevokedAt })
                .HasDatabaseName("ix_refresh_tokens_session_revoked");
            entity.HasOne(token => token.AuthSession)
                .WithMany(session => session.RefreshTokens)
                .HasForeignKey(token => token.AuthSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles", table =>
            {
                table.HasCheckConstraint("ck_user_profiles_age", "age BETWEEN 13 AND 120");
                table.HasCheckConstraint(
                    "ck_user_profiles_height",
                    "height_centimeters > 0 AND height_centimeters <= 300");
            });
            entity.HasKey(profile => profile.UserId).HasName("pk_user_profiles");
            entity.Property(profile => profile.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(profile => profile.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            entity.Property(profile => profile.Age).HasColumnName("age");
            entity.Property(profile => profile.Goal).HasColumnName("goal").HasMaxLength(64).IsRequired();
            entity.Property(profile => profile.HeightCentimeters).HasColumnName("height_centimeters").HasPrecision(6, 2);
            entity.Property(profile => profile.CreatedAt).HasColumnName("created_at");
            entity.Property(profile => profile.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(profile => profile.UpdatedAt).HasDatabaseName("ix_user_profiles_updated_at");
        });

        modelBuilder.Entity<HealthMetric>(entity =>
        {
            entity.ToTable("health_metrics", table =>
            {
                table.HasCheckConstraint("ck_health_metrics_value", "value >= 0");
            });
            entity.HasKey(metric => metric.Id).HasName("pk_health_metrics");
            entity.Property(metric => metric.Id).HasColumnName("id");
            entity.Property(metric => metric.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(metric => metric.Metric).HasColumnName("metric").HasMaxLength(48).IsRequired();
            entity.Property(metric => metric.Unit).HasColumnName("unit").HasMaxLength(24).IsRequired();
            entity.Property(metric => metric.Value).HasColumnName("value").HasPrecision(16, 4);
            entity.Property(metric => metric.ObservedAt).HasColumnName("observed_at");
            entity.Property(metric => metric.CreatedAt).HasColumnName("created_at");
            entity.Property(metric => metric.UpdatedAt).HasColumnName("updated_at");
            entity.Property(metric => metric.Source).HasColumnName("source").HasMaxLength(64).IsRequired();
            entity.Property(metric => metric.ExternalId).HasColumnName("external_id").HasMaxLength(128).IsRequired();
            entity.Property(metric => metric.DeviceId).HasColumnName("device_id").HasMaxLength(128).IsRequired();
            entity.Property(metric => metric.SyncVersion).HasColumnName("sync_version").IsRequired();
            entity.HasIndex(metric => new { metric.UserId, metric.Source, metric.ExternalId })
                .IsUnique()
                .HasDatabaseName("ux_health_metrics_user_source_record");
            entity.HasIndex(metric => new { metric.UserId, metric.Metric, metric.ObservedAt })
                .HasDatabaseName("ix_health_metrics_user_id_metric_observed_at");
            entity.HasIndex(metric => new { metric.UserId, metric.ObservedAt })
                .HasDatabaseName("ix_health_metrics_user_id_observed_at");
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("ai_conversations", table =>
            {
                table.HasCheckConstraint("ck_ai_conversations_token_counts", "input_tokens >= 0 AND output_tokens >= 0");
                table.HasCheckConstraint("ck_ai_conversations_estimated_cost", "estimated_cost >= 0");
            });
            entity.HasKey(conversation => conversation.Id).HasName("pk_ai_conversations");
            entity.Property(conversation => conversation.Id).HasColumnName("id");
            entity.Property(conversation => conversation.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(conversation => conversation.CreatedAt).HasColumnName("created_at");
            entity.Property(conversation => conversation.UpdatedAt).HasColumnName("updated_at");
            entity.Property(conversation => conversation.ContextVersion).HasColumnName("context_version");
            entity.Property(conversation => conversation.InputTokens).HasColumnName("input_tokens");
            entity.Property(conversation => conversation.OutputTokens).HasColumnName("output_tokens");
            entity.Property(conversation => conversation.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(18, 8);
            entity.HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAt })
                .HasDatabaseName("ix_ai_conversations_user_updated_at");
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(conversation => conversation.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("ai_messages", table =>
                table.HasCheckConstraint("ck_ai_messages_role", "role IN ('User', 'Assistant')"));
            entity.HasKey(message => message.Id).HasName("pk_ai_messages");
            entity.Property(message => message.Id).HasColumnName("id");
            entity.Property(message => message.ConversationId).HasColumnName("conversation_id");
            entity.Property(message => message.Role)
                .HasColumnName("role")
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(message => message.Content).HasColumnName("content").HasColumnType("text").IsRequired();
            entity.Property(message => message.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(message => new { message.ConversationId, message.CreatedAt })
                .HasDatabaseName("ix_ai_messages_conversation_created_at");
            entity.HasOne<Conversation>()
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AITokenUsage>(entity =>
        {
            entity.ToTable("ai_token_usage", table =>
            {
                table.HasCheckConstraint("ck_ai_token_usage_counts", "input_tokens >= 0 AND output_tokens >= 0");
                table.HasCheckConstraint("ck_ai_token_usage_estimated_cost", "estimated_cost >= 0");
            });
            entity.HasKey(usage => usage.Id).HasName("pk_ai_token_usage");
            entity.Property(usage => usage.Id).HasColumnName("id");
            entity.Property(usage => usage.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(usage => usage.ConversationId).HasColumnName("conversation_id");
            entity.Property(usage => usage.Model).HasColumnName("model").HasMaxLength(128).IsRequired();
            entity.Property(usage => usage.InputTokens).HasColumnName("input_tokens");
            entity.Property(usage => usage.OutputTokens).HasColumnName("output_tokens");
            entity.Property(usage => usage.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(18, 8);
            entity.Property(usage => usage.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(usage => new { usage.UserId, usage.CreatedAt })
                .HasDatabaseName("ix_ai_token_usage_user_created_at");
            entity.HasIndex(usage => new { usage.ConversationId, usage.CreatedAt })
                .HasDatabaseName("ix_ai_token_usage_conversation_created_at");
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(usage => usage.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Conversation>()
                .WithMany()
                .HasForeignKey(usage => usage.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AIContextSnapshot>(entity =>
        {
            entity.ToTable("ai_context_snapshots");
            entity.HasKey(snapshot => snapshot.Id).HasName("pk_ai_context_snapshots");
            entity.Property(snapshot => snapshot.Id).HasColumnName("id");
            entity.Property(snapshot => snapshot.ConversationId).HasColumnName("conversation_id");
            entity.Property(snapshot => snapshot.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(snapshot => snapshot.ContextVersion).HasColumnName("context_version");
            entity.Property(snapshot => snapshot.Json).HasColumnName("context_json").HasColumnType("text").IsRequired();
            entity.Property(snapshot => snapshot.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(snapshot => new { snapshot.ConversationId, snapshot.CreatedAt })
                .HasDatabaseName("ix_ai_context_snapshots_conversation_created_at");
            entity.HasOne<Conversation>()
                .WithMany()
                .HasForeignKey(snapshot => snapshot.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(snapshot => snapshot.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AIConsent>(entity =>
        {
            entity.ToTable("ai_consents");
            entity.HasKey(consent => consent.UserId).HasName("pk_ai_consents");
            entity.Property(consent => consent.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(consent => consent.Granted).HasColumnName("granted");
            entity.Property(consent => consent.Version).HasColumnName("version");
            entity.Property(consent => consent.GrantedAt).HasColumnName("granted_at");
            entity.Property(consent => consent.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<User>().WithMany().HasForeignKey(consent => consent.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AICoachRequest>(entity =>
        {
            entity.ToTable("ai_coach_requests", table =>
                table.HasCheckConstraint("ck_ai_coach_requests_status", "status IN ('Processing', 'Completed')"));
            entity.HasKey(request => new { request.UserId, request.ClientRequestId })
                .HasName("pk_ai_coach_requests");
            entity.Property(request => request.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(request => request.ClientRequestId).HasColumnName("client_request_id");
            entity.Property(request => request.ConversationId).HasColumnName("conversation_id");
            entity.Property(request => request.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(request => request.ResponseJson).HasColumnName("response_json").HasColumnType("text");
            entity.Property(request => request.CreatedAt).HasColumnName("created_at");
            entity.Property(request => request.CompletedAt).HasColumnName("completed_at");
            entity.HasIndex(request => request.CreatedAt).HasDatabaseName("ix_ai_coach_requests_created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(request => request.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Conversation>().WithMany().HasForeignKey(request => request.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AIConversationSummaryRecord>(entity =>
        {
            entity.ToTable("ai_conversation_summaries");
            entity.HasKey(summary => summary.ConversationId).HasName("pk_ai_conversation_summaries");
            entity.Property(summary => summary.ConversationId).HasColumnName("conversation_id");
            entity.Property(summary => summary.Summary).HasColumnName("summary").HasMaxLength(1200).IsRequired();
            entity.Property(summary => summary.ContextVersion).HasColumnName("context_version");
            entity.Property(summary => summary.PromptVersion).HasColumnName("prompt_version").HasMaxLength(32).IsRequired();
            entity.Property(summary => summary.MessageCountIncluded).HasColumnName("message_count_included");
            entity.Property(summary => summary.CreatedAt).HasColumnName("created_at");
            entity.Property(summary => summary.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(summary => summary.UpdatedAt).HasDatabaseName("ix_ai_conversation_summaries_updated_at");
            entity.HasOne<Conversation>().WithMany().HasForeignKey(summary => summary.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AIFeedback>(entity =>
        {
            entity.ToTable("ai_feedback", table =>
                table.HasCheckConstraint("ck_ai_feedback_rating", "rating IN ('Helpful', 'NotHelpful')"));
            entity.HasKey(feedback => feedback.Id).HasName("pk_ai_feedback");
            entity.Property(feedback => feedback.Id).HasColumnName("id");
            entity.Property(feedback => feedback.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(feedback => feedback.ConversationId).HasColumnName("conversation_id");
            entity.Property(feedback => feedback.MessageId).HasColumnName("message_id");
            entity.Property(feedback => feedback.Rating).HasColumnName("rating").HasMaxLength(16).IsRequired();
            entity.Property(feedback => feedback.ReasonCode).HasColumnName("reason_code").HasMaxLength(32);
            entity.Property(feedback => feedback.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(feedback => new { feedback.UserId, feedback.CreatedAt })
                .HasDatabaseName("ix_ai_feedback_user_created_at");
            entity.HasIndex(feedback => new { feedback.UserId, feedback.MessageId }).IsUnique()
                .HasDatabaseName("ux_ai_feedback_user_message");
            entity.HasOne<User>().WithMany().HasForeignKey(feedback => feedback.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Conversation>().WithMany().HasForeignKey(feedback => feedback.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Message>().WithMany().HasForeignKey(feedback => feedback.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutPlan>(entity =>
        {
            entity.ToTable("workout_plans", table =>
                table.HasCheckConstraint("ck_workout_plans_dates", "ends_on >= starts_on"));
            entity.HasKey(plan => plan.Id).HasName("pk_workout_plans");
            entity.Property(plan => plan.Id).HasColumnName("id");
            entity.Property(plan => plan.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(plan => plan.Goal).HasColumnName("goal").HasMaxLength(64).IsRequired();
            entity.Property(plan => plan.StartsOn).HasColumnName("starts_on");
            entity.Property(plan => plan.EndsOn).HasColumnName("ends_on");
            entity.Property(plan => plan.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(plan => new { plan.UserId, plan.CreatedAt })
                .HasDatabaseName("ix_workout_plans_user_created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(plan => plan.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(plan => plan.Days).WithOne().HasForeignKey(day => day.WorkoutPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutDay>(entity =>
        {
            entity.ToTable("workout_days");
            entity.HasKey(day => day.Id).HasName("pk_workout_days");
            entity.Property(day => day.Id).HasColumnName("id");
            entity.Property(day => day.WorkoutPlanId).HasColumnName("workout_plan_id");
            entity.Property(day => day.Date).HasColumnName("date");
            entity.Property(day => day.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            entity.Property(day => day.RecoveryNote).HasColumnName("recovery_note").HasMaxLength(1000).IsRequired();
            entity.HasIndex(day => new { day.WorkoutPlanId, day.Date }).IsUnique()
                .HasDatabaseName("ux_workout_days_plan_date");
            entity.HasMany(day => day.Exercises).WithOne().HasForeignKey(exercise => exercise.WorkoutDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutExercise>(entity =>
        {
            entity.ToTable("workout_exercises", table =>
            {
                table.HasCheckConstraint("ck_workout_exercises_position", "position >= 0");
                table.HasCheckConstraint(
                    "ck_workout_exercises_duration",
                    "duration_minutes IS NULL OR duration_minutes BETWEEN 1 AND 240");
            });
            entity.HasKey(exercise => exercise.Id).HasName("pk_workout_exercises");
            entity.Property(exercise => exercise.Id).HasColumnName("id");
            entity.Property(exercise => exercise.WorkoutDayId).HasColumnName("workout_day_id");
            entity.Property(exercise => exercise.Position).HasColumnName("position");
            entity.Property(exercise => exercise.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            entity.Property(exercise => exercise.Sets).HasColumnName("sets").HasMaxLength(32);
            entity.Property(exercise => exercise.Repetitions).HasColumnName("repetitions").HasMaxLength(32);
            entity.Property(exercise => exercise.DurationMinutes).HasColumnName("duration_minutes");
            entity.Property(exercise => exercise.RestSeconds).HasColumnName("rest_seconds");
            entity.Property(exercise => exercise.Instructions).HasColumnName("instructions").HasMaxLength(500);
            entity.HasIndex(exercise => new { exercise.WorkoutDayId, exercise.Position }).IsUnique()
                .HasDatabaseName("ux_workout_exercises_day_position");
        });

        modelBuilder.Entity<WorkoutCompletion>(entity =>
        {
            entity.ToTable("workout_completions");
            entity.HasKey(completion => completion.Id).HasName("pk_workout_completions");
            entity.Property(completion => completion.Id).HasColumnName("id");
            entity.Property(completion => completion.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(completion => completion.WorkoutDayId).HasColumnName("workout_day_id");
            entity.Property(completion => completion.Completed).HasColumnName("completed");
            entity.Property(completion => completion.RecordedAt).HasColumnName("recorded_at");
            entity.HasIndex(completion => new { completion.UserId, completion.RecordedAt })
                .HasDatabaseName("ix_workout_completions_user_recorded_at");
            entity.HasOne<User>().WithMany().HasForeignKey(completion => completion.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WorkoutDay>().WithMany().HasForeignKey(completion => completion.WorkoutDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutFeedback>(entity =>
        {
            entity.ToTable("workout_feedback", table =>
                table.HasCheckConstraint(
                    "ck_workout_feedback_difficulty",
                    "difficulty IN ('TooEasy', 'Appropriate', 'TooHard', 'VeryEasy', 'Easy', 'Hard', 'VeryHard')"));
            entity.HasKey(feedback => feedback.Id).HasName("pk_workout_feedback");
            entity.Property(feedback => feedback.Id).HasColumnName("id");
            entity.Property(feedback => feedback.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(feedback => feedback.WorkoutDayId).HasColumnName("workout_day_id");
            entity.Property(feedback => feedback.Completed).HasColumnName("completed");
            entity.Property(feedback => feedback.Difficulty).HasColumnName("difficulty").HasConversion<string>().HasMaxLength(16);
            entity.Property(feedback => feedback.Comment).HasColumnName("comment").HasMaxLength(500);
            entity.Property(feedback => feedback.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(feedback => new { feedback.UserId, feedback.CreatedAt })
                .HasDatabaseName("ix_workout_feedback_user_created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(feedback => feedback.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WorkoutDay>().WithMany().HasForeignKey(feedback => feedback.WorkoutDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutRecommendation>(entity =>
        {
            entity.ToTable("workout_recommendations");
            entity.HasKey(recommendation => recommendation.Id).HasName("pk_workout_recommendations");
            entity.Property(recommendation => recommendation.Id).HasColumnName("id");
            entity.Property(recommendation => recommendation.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(recommendation => recommendation.WorkoutPlanId).HasColumnName("workout_plan_id");
            entity.Property(recommendation => recommendation.Type).HasColumnName("type").HasMaxLength(32).IsRequired();
            entity.Property(recommendation => recommendation.Text).HasColumnName("text").HasMaxLength(2000).IsRequired();
            entity.Property(recommendation => recommendation.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(recommendation => new { recommendation.UserId, recommendation.CreatedAt })
                .HasDatabaseName("ix_workout_recommendations_user_created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(recommendation => recommendation.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WorkoutPlan>().WithMany().HasForeignKey(recommendation => recommendation.WorkoutPlanId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Goal>(entity =>
        {
            entity.ToTable("goals", table =>
            {
                table.HasCheckConstraint(
                    "ck_goals_type",
                    "type IN ('FatLoss', 'MuscleGain', 'WeightMaintenance', 'Endurance', 'GeneralHealth')");
                table.HasCheckConstraint(
                    "ck_goals_status",
                    "status IN ('Active', 'Completed', 'Paused', 'Abandoned')");
                table.HasCheckConstraint(
                    "ck_goals_target_weight",
                    "target_weight_kilograms IS NULL OR target_weight_kilograms BETWEEN 20 AND 500");
            });
            entity.HasKey(goal => goal.Id).HasName("pk_goals");
            entity.Property(goal => goal.Id).HasColumnName("id");
            entity.Property(goal => goal.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(goal => goal.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(32);
            entity.Property(goal => goal.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            entity.Property(goal => goal.TargetWeightKilograms).HasColumnName("target_weight_kilograms").HasPrecision(7, 2);
            entity.Property(goal => goal.StartsOn).HasColumnName("starts_on");
            entity.Property(goal => goal.TargetDate).HasColumnName("target_date");
            entity.Property(goal => goal.CreatedAt).HasColumnName("created_at");
            entity.Property(goal => goal.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(goal => new { goal.UserId, goal.Status, goal.CreatedAt })
                .HasDatabaseName("ix_goals_user_status_created_at");
            entity.HasIndex(goal => goal.UserId).IsUnique()
                .HasFilter("status = 'Active'")
                .HasDatabaseName("ux_goals_one_active_per_user");
            entity.HasOne<User>().WithMany().HasForeignKey(goal => goal.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(goal => goal.Progress).WithOne().HasForeignKey(progress => progress.GoalId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoalProgress>(entity =>
        {
            entity.ToTable("goal_progress", table =>
                table.HasCheckConstraint(
                    "ck_goal_progress_completion",
                    "completion_percent IS NULL OR completion_percent BETWEEN 0 AND 100"));
            entity.HasKey(progress => progress.Id).HasName("pk_goal_progress");
            entity.Property(progress => progress.Id).HasColumnName("id");
            entity.Property(progress => progress.GoalId).HasColumnName("goal_id");
            entity.Property(progress => progress.Date).HasColumnName("date");
            entity.Property(progress => progress.WeightKilograms).HasColumnName("weight_kilograms").HasPrecision(7, 2);
            entity.Property(progress => progress.CompletionPercent).HasColumnName("completion_percent").HasPrecision(6, 2);
            entity.Property(progress => progress.Steps).HasColumnName("steps").HasPrecision(14, 2);
            entity.Property(progress => progress.SleepHours).HasColumnName("sleep_hours").HasPrecision(5, 2);
            entity.Property(progress => progress.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(progress => new { progress.GoalId, progress.Date }).IsUnique()
                .HasDatabaseName("ux_goal_progress_goal_date");
        });

        modelBuilder.Entity<NutritionProfile>(entity =>
        {
            entity.ToTable("nutrition_profiles", table =>
            {
                table.HasCheckConstraint("ck_nutrition_profiles_sex", "sex IN ('Male', 'Female')");
                table.HasCheckConstraint(
                    "ck_nutrition_profiles_activity",
                    "activity_level IN ('Sedentary', 'Light', 'Moderate', 'High', 'Athlete')");
            });
            entity.HasKey(profile => profile.UserId).HasName("pk_nutrition_profiles");
            entity.Property(profile => profile.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(profile => profile.Sex).HasColumnName("sex").HasConversion<string>().HasMaxLength(8);
            entity.Property(profile => profile.ActivityLevel).HasColumnName("activity_level").HasConversion<string>().HasMaxLength(16);
            entity.Property(profile => profile.CreatedAt).HasColumnName("created_at");
            entity.Property(profile => profile.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<User>().WithMany().HasForeignKey(profile => profile.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NutritionLog>(entity =>
        {
            entity.ToTable("nutrition_logs", table =>
            {
                table.HasCheckConstraint("ck_nutrition_logs_meal_type", "meal_type IN ('Breakfast', 'Lunch', 'Dinner', 'Snack')");
                table.HasCheckConstraint(
                    "ck_nutrition_logs_amounts",
                    "calories >= 0 AND protein_grams >= 0 AND fat_grams >= 0 AND carbohydrate_grams >= 0");
            });
            entity.HasKey(log => log.Id).HasName("pk_nutrition_logs");
            entity.Property(log => log.Id).HasColumnName("id");
            entity.Property(log => log.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(log => log.MealType).HasColumnName("meal_type").HasConversion<string>().HasMaxLength(16);
            entity.Property(log => log.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            entity.Property(log => log.Calories).HasColumnName("calories").HasPrecision(10, 2);
            entity.Property(log => log.ProteinGrams).HasColumnName("protein_grams").HasPrecision(8, 2);
            entity.Property(log => log.FatGrams).HasColumnName("fat_grams").HasPrecision(8, 2);
            entity.Property(log => log.CarbohydrateGrams).HasColumnName("carbohydrate_grams").HasPrecision(8, 2);
            entity.Property(log => log.Date).HasColumnName("date");
            entity.Property(log => log.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(log => new { log.UserId, log.Date, log.MealType })
                .HasDatabaseName("ix_nutrition_logs_user_date_meal");
            entity.HasOne<User>().WithMany().HasForeignKey(log => log.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MacroTarget>(entity =>
        {
            entity.ToTable("macro_targets", table =>
                table.HasCheckConstraint(
                    "ck_macro_targets_amounts",
                    "calories >= 0 AND protein_grams >= 0 AND fat_grams >= 0 AND carbohydrate_grams >= 0"));
            entity.HasKey(target => target.Id).HasName("pk_macro_targets");
            entity.Property(target => target.Id).HasColumnName("id");
            entity.Property(target => target.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(target => target.GoalId).HasColumnName("goal_id");
            entity.Property(target => target.Calories).HasColumnName("calories").HasPrecision(10, 2);
            entity.Property(target => target.ProteinGrams).HasColumnName("protein_grams").HasPrecision(8, 2);
            entity.Property(target => target.FatGrams).HasColumnName("fat_grams").HasPrecision(8, 2);
            entity.Property(target => target.CarbohydrateGrams).HasColumnName("carbohydrate_grams").HasPrecision(8, 2);
            entity.Property(target => target.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(target => target.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(target => new { target.UserId, target.EffectiveFrom })
                .HasDatabaseName("ix_macro_targets_user_effective_from");
            entity.HasOne<User>().WithMany().HasForeignKey(target => target.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Goal>().WithMany().HasForeignKey(target => target.GoalId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DailyNutritionSummary>(entity =>
        {
            entity.ToTable("daily_nutrition_summaries", table =>
                table.HasCheckConstraint(
                    "ck_daily_nutrition_summaries_amounts",
                    "calories >= 0 AND protein_grams >= 0 AND fat_grams >= 0 AND carbohydrate_grams >= 0"));
            entity.HasKey(summary => summary.Id).HasName("pk_daily_nutrition_summaries");
            entity.Property(summary => summary.Id).HasColumnName("id");
            entity.Property(summary => summary.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(summary => summary.Date).HasColumnName("date");
            entity.Property(summary => summary.Calories).HasColumnName("calories").HasPrecision(10, 2);
            entity.Property(summary => summary.ProteinGrams).HasColumnName("protein_grams").HasPrecision(8, 2);
            entity.Property(summary => summary.FatGrams).HasColumnName("fat_grams").HasPrecision(8, 2);
            entity.Property(summary => summary.CarbohydrateGrams).HasColumnName("carbohydrate_grams").HasPrecision(8, 2);
            entity.Property(summary => summary.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(summary => new { summary.UserId, summary.Date }).IsUnique()
                .HasDatabaseName("ux_daily_nutrition_summaries_user_date");
            entity.HasOne<User>().WithMany().HasForeignKey(summary => summary.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserInsights>(entity =>
        {
            entity.ToTable("user_insights", table => table.HasCheckConstraint(
                "ck_user_insights_adherence", "training_adherence_rate IS NULL OR training_adherence_rate BETWEEN 0 AND 1"));
            entity.HasKey(item => item.Id).HasName("pk_user_insights");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.Date).HasColumnName("date");
            entity.Property(item => item.WeightChangeKilograms).HasColumnName("weight_change_kilograms").HasPrecision(8, 2);
            entity.Property(item => item.SleepChangeHours).HasColumnName("sleep_change_hours").HasPrecision(7, 2);
            entity.Property(item => item.StepsChangePercent).HasColumnName("steps_change_percent").HasPrecision(8, 2);
            entity.Property(item => item.HeartRateChange).HasColumnName("heart_rate_change").HasPrecision(7, 2);
            entity.Property(item => item.ActiveCaloriesChange).HasColumnName("active_calories_change").HasPrecision(10, 2);
            entity.Property(item => item.TotalCaloriesChange).HasColumnName("total_calories_change").HasPrecision(10, 2);
            entity.Property(item => item.TrainingAdherenceRate).HasColumnName("training_adherence_rate").HasPrecision(5, 4);
            entity.Property(item => item.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
            entity.Property(item => item.GeneratedAt).HasColumnName("generated_at");
            entity.HasIndex(item => new { item.UserId, item.Date }).IsUnique()
                .HasDatabaseName("ux_user_insights_user_date");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BehaviorPattern>(entity =>
        {
            entity.ToTable("behavior_patterns", table =>
            {
                table.HasCheckConstraint("ck_behavior_patterns_confidence", "confidence BETWEEN 0 AND 1");
                table.HasCheckConstraint("ck_behavior_patterns_evidence", "evidence_count >= 0");
            });
            entity.HasKey(item => item.Id).HasName("pk_behavior_patterns");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.Key).HasColumnName("key").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
            entity.Property(item => item.EvidenceCount).HasColumnName("evidence_count");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(item => new { item.UserId, item.Key }).IsUnique()
                .HasDatabaseName("ux_behavior_patterns_user_key");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrainingAdherence>(entity =>
        {
            entity.ToTable("training_adherence", table =>
            {
                table.HasCheckConstraint(
                    "ck_training_adherence_counts",
                    "planned_sessions >= 0 AND completed_sessions >= 0 AND completed_sessions <= planned_sessions");
                table.HasCheckConstraint("ck_training_adherence_rate", "rate BETWEEN 0 AND 1");
                table.HasCheckConstraint("ck_training_adherence_period", "period_end >= period_start");
            });
            entity.HasKey(item => item.Id).HasName("pk_training_adherence");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.PeriodStart).HasColumnName("period_start");
            entity.Property(item => item.PeriodEnd).HasColumnName("period_end");
            entity.Property(item => item.PlannedSessions).HasColumnName("planned_sessions");
            entity.Property(item => item.CompletedSessions).HasColumnName("completed_sessions");
            entity.Property(item => item.Rate).HasColumnName("rate").HasPrecision(5, 4);
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(item => new { item.UserId, item.PeriodStart }).IsUnique()
                .HasDatabaseName("ux_training_adherence_user_period");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoalAchievement>(entity =>
        {
            entity.ToTable("goal_achievements", table => table.HasCheckConstraint(
                "ck_goal_achievements_progress", "progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100"));
            entity.HasKey(item => item.Id).HasName("pk_goal_achievements");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.GoalId).HasColumnName("goal_id");
            entity.Property(item => item.Goal).HasColumnName("goal").HasMaxLength(32).IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(item => item.ProgressPercent).HasColumnName("progress_percent").HasPrecision(6, 2);
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(item => new { item.UserId, item.GoalId }).IsUnique()
                .HasDatabaseName("ux_goal_achievements_user_goal");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Goal>().WithMany().HasForeignKey(item => item.GoalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecommendationHistory>(entity =>
        {
            entity.ToTable("recommendation_history");
            entity.HasKey(item => item.Id).HasName("pk_recommendation_history");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.WorkoutPlanId).HasColumnName("workout_plan_id");
            entity.Property(item => item.Type).HasColumnName("type").HasMaxLength(32).IsRequired();
            entity.Property(item => item.Text).HasColumnName("text").HasMaxLength(2000).IsRequired();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => new { item.UserId, item.CreatedAt })
                .HasDatabaseName("ix_recommendation_history_user_created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WorkoutPlan>().WithMany().HasForeignKey(item => item.WorkoutPlanId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RecommendationOutcome>(entity =>
        {
            entity.ToTable("recommendation_outcomes", table => table.HasCheckConstraint(
                "ck_recommendation_outcomes_score", "score BETWEEN 0 AND 1"));
            entity.HasKey(item => item.Id).HasName("pk_recommendation_outcomes");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.RecommendationHistoryId).HasColumnName("recommendation_history_id");
            entity.Property(item => item.Completed).HasColumnName("completed");
            entity.Property(item => item.Feedback).HasColumnName("feedback").HasMaxLength(500).IsRequired();
            entity.Property(item => item.Score).HasColumnName("score").HasPrecision(5, 4);
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => new { item.UserId, item.RecommendationHistoryId, item.CreatedAt })
                .HasDatabaseName("ix_recommendation_outcomes_user_recommendation_created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RecommendationHistory>().WithMany().HasForeignKey(item => item.RecommendationHistoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductEvent>(entity =>
        {
            entity.ToTable("product_events");
            entity.HasKey(item => item.Id).HasName("pk_product_events");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(48).IsRequired();
            entity.Property(item => item.OccurredAt).HasColumnName("occurred_at");
            entity.Property(item => item.ReceivedAt).HasColumnName("received_at");
            entity.Property(item => item.AppVersion).HasColumnName("app_version").HasMaxLength(40);
            entity.Property(item => item.PropertiesJson).HasColumnName("properties").HasMaxLength(4000).IsRequired();
            entity.Ignore(item => item.Properties);
            entity.HasIndex(item => new { item.UserId, item.OccurredAt })
                .HasDatabaseName("ix_product_events_user_occurred");
            entity.HasIndex(item => new { item.Name, item.OccurredAt })
                .HasDatabaseName("ix_product_events_name_occurred");
            entity.HasIndex(item => item.OccurredAt)
                .HasDatabaseName("ix_product_events_occurred");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupportReport>(entity =>
        {
            entity.ToTable("support_reports", table =>
            {
                table.HasCheckConstraint("ck_support_reports_kind", "kind IN ('feedback', 'bug', 'support')");
                table.HasCheckConstraint("ck_support_reports_rating", "rating IS NULL OR rating BETWEEN 1 AND 5");
            });
            entity.HasKey(item => item.Id).HasName("pk_support_reports");
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
            entity.Property(item => item.Category).HasColumnName("category")
                .HasMaxLength(SupportReport.MaximumCategoryLength).IsRequired();
            entity.Property(item => item.Message).HasColumnName("message")
                .HasMaxLength(SupportReport.MaximumMessageLength).IsRequired();
            entity.Property(item => item.Rating).HasColumnName("rating");
            entity.Property(item => item.ContactEmail).HasColumnName("contact_email")
                .HasMaxLength(SupportReport.MaximumEmailLength);
            entity.Property(item => item.AppVersion).HasColumnName("app_version")
                .HasMaxLength(SupportReport.MaximumMetadataLength);
            entity.Property(item => item.BuildNumber).HasColumnName("build_number")
                .HasMaxLength(SupportReport.MaximumMetadataLength);
            entity.Property(item => item.OsVersion).HasColumnName("os_version")
                .HasMaxLength(SupportReport.MaximumMetadataLength);
            entity.Property(item => item.DeviceModel).HasColumnName("device_model")
                .HasMaxLength(SupportReport.MaximumMetadataLength);
            entity.Property(item => item.DiagnosticsJson).HasColumnName("diagnostics")
                .HasMaxLength(SupportReport.MaximumDiagnosticsLength).IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => new { item.UserId, item.CreatedAt })
                .HasDatabaseName("ix_support_reports_user_created");
            entity.HasIndex(item => new { item.Kind, item.CreatedAt })
                .HasDatabaseName("ix_support_reports_kind_created");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserConsent>(entity =>
        {
            entity.ToTable("user_consents", table =>
                table.HasCheckConstraint("ck_user_consents_kind", "kind IN ('health_data', 'analytics')"));
            entity.HasKey(item => new { item.UserId, item.Kind }).HasName("pk_user_consents");
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128);
            entity.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(32);
            entity.Property(item => item.Granted).HasColumnName("granted");
            entity.Property(item => item.Version).HasColumnName("version");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
