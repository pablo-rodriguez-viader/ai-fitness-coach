using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261018230001_AddCoachV3Insights")]
public sealed class AddCoachV3Insights : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_insights",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                weight_change_kilograms = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                sleep_change_hours = table.Column<double>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                steps_change_percent = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                heart_rate_change = table.Column<double>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                active_calories_change = table.Column<double>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                total_calories_change = table.Column<double>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                training_adherence_rate = table.Column<double>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_insights", x => x.id);
                table.CheckConstraint(
                    "ck_user_insights_adherence",
                    "training_adherence_rate IS NULL OR training_adherence_rate BETWEEN 0 AND 1");
                table.ForeignKey(
                    name: "fk_user_insights_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "behavior_patterns",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                confidence = table.Column<double>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                evidence_count = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_behavior_patterns", x => x.id);
                table.CheckConstraint("ck_behavior_patterns_confidence", "confidence BETWEEN 0 AND 1");
                table.CheckConstraint("ck_behavior_patterns_evidence", "evidence_count >= 0");
                table.ForeignKey(
                    name: "fk_behavior_patterns_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "training_adherence",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                period_start = table.Column<DateOnly>(type: "date", nullable: false),
                period_end = table.Column<DateOnly>(type: "date", nullable: false),
                planned_sessions = table.Column<int>(type: "integer", nullable: false),
                completed_sessions = table.Column<int>(type: "integer", nullable: false),
                rate = table.Column<double>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_training_adherence", x => x.id);
                table.CheckConstraint(
                    "ck_training_adherence_counts",
                    "planned_sessions >= 0 AND completed_sessions >= 0 AND completed_sessions <= planned_sessions");
                table.CheckConstraint("ck_training_adherence_rate", "rate BETWEEN 0 AND 1");
                table.CheckConstraint("ck_training_adherence_period", "period_end >= period_start");
                table.ForeignKey(
                    name: "fk_training_adherence_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "goal_achievements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                goal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                progress_percent = table.Column<double>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goal_achievements", x => x.id);
                table.CheckConstraint(
                    "ck_goal_achievements_progress",
                    "progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100");
                table.ForeignKey(
                    name: "fk_goal_achievements_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_goal_achievements_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "recommendation_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                workout_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_recommendation_history", x => x.id);
                table.ForeignKey(
                    name: "fk_recommendation_history_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_recommendation_history_workout_plans_workout_plan_id",
                    column: x => x.workout_plan_id,
                    principalTable: "workout_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "recommendation_outcomes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                recommendation_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                completed = table.Column<bool>(type: "boolean", nullable: true),
                feedback = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                score = table.Column<double>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_recommendation_outcomes", x => x.id);
                table.CheckConstraint("ck_recommendation_outcomes_score", "score BETWEEN 0 AND 1");
                table.ForeignKey(
                    name: "fk_recommendation_outcomes_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_recommendation_outcomes_recommendation_history_recommendation_history_id",
                    column: x => x.recommendation_history_id,
                    principalTable: "recommendation_history",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_user_insights_user_date",
            table: "user_insights",
            columns: new[] { "user_id", "date" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ux_behavior_patterns_user_key",
            table: "behavior_patterns",
            columns: new[] { "user_id", "key" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ux_training_adherence_user_period",
            table: "training_adherence",
            columns: new[] { "user_id", "period_start" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ux_goal_achievements_user_goal",
            table: "goal_achievements",
            columns: new[] { "user_id", "goal_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_goal_achievements_goal_id",
            table: "goal_achievements",
            column: "goal_id");
        migrationBuilder.CreateIndex(
            name: "ix_recommendation_history_user_created_at",
            table: "recommendation_history",
            columns: new[] { "user_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ix_recommendation_history_workout_plan_id",
            table: "recommendation_history",
            column: "workout_plan_id");
        migrationBuilder.CreateIndex(
            name: "ix_recommendation_outcomes_user_recommendation_created_at",
            table: "recommendation_outcomes",
            columns: new[] { "user_id", "recommendation_history_id", "created_at" });

        migrationBuilder.Sql("""
            INSERT INTO recommendation_history (id, user_id, workout_plan_id, type, text, created_at)
            SELECT id, user_id, workout_plan_id, type, text, created_at
            FROM workout_recommendations
            """);
        migrationBuilder.Sql("""
            INSERT INTO recommendation_outcomes
                (id, user_id, recommendation_history_id, completed, feedback, score, created_at)
            SELECT gen_random_uuid(),
                   feedback.user_id,
                   recommendation.id,
                   feedback.completed,
                   'workout_feedback:' || feedback.difficulty,
                   CASE
                       WHEN NOT feedback.completed THEN 0.1
                       WHEN feedback.difficulty = 'TooHard' THEN 0.25
                       WHEN feedback.difficulty = 'TooEasy' THEN 0.7
                       ELSE 0.9
                   END,
                   feedback.created_at
            FROM workout_feedback AS feedback
            JOIN workout_days AS day ON day.id = feedback.workout_day_id
            JOIN recommendation_history AS recommendation
                ON recommendation.workout_plan_id = day.workout_plan_id
               AND recommendation.user_id = feedback.user_id
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "behavior_patterns");
        migrationBuilder.DropTable(name: "goal_achievements");
        migrationBuilder.DropTable(name: "recommendation_outcomes");
        migrationBuilder.DropTable(name: "training_adherence");
        migrationBuilder.DropTable(name: "user_insights");
        migrationBuilder.DropTable(name: "recommendation_history");
    }
}
