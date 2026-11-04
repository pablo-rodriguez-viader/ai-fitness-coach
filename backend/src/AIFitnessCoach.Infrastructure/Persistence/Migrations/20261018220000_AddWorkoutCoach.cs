using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261018220000_AddWorkoutCoach")]
public sealed class AddWorkoutCoach : Migration
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
            name: "workout_plans",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                goal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workout_plans", x => x.id);
                table.CheckConstraint("ck_workout_plans_dates", "ends_on >= starts_on");
                table.ForeignKey(
                    name: "fk_workout_plans_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "workout_days",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workout_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                recovery_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workout_days", x => x.id);
                table.ForeignKey(
                    name: "fk_workout_days_workout_plans_workout_plan_id",
                    column: x => x.workout_plan_id,
                    principalTable: "workout_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "workout_recommendations",
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
                table.PrimaryKey("pk_workout_recommendations", x => x.id);
                table.ForeignKey(
                    name: "fk_workout_recommendations_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_workout_recommendations_workout_plans_workout_plan_id",
                    column: x => x.workout_plan_id,
                    principalTable: "workout_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "workout_completions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                workout_day_id = table.Column<Guid>(type: "uuid", nullable: false),
                completed = table.Column<bool>(type: "boolean", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workout_completions", x => x.id);
                table.ForeignKey(
                    name: "fk_workout_completions_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_workout_completions_workout_days_workout_day_id",
                    column: x => x.workout_day_id,
                    principalTable: "workout_days",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "workout_exercises",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workout_day_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                sets = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                repetitions = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                duration_minutes = table.Column<int>(type: "integer", nullable: true),
                rest_seconds = table.Column<int>(type: "integer", nullable: true),
                instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workout_exercises", x => x.id);
                table.CheckConstraint("ck_workout_exercises_position", "position >= 0");
                table.CheckConstraint(
                    "ck_workout_exercises_duration",
                    "duration_minutes IS NULL OR duration_minutes BETWEEN 1 AND 240");
                table.ForeignKey(
                    name: "fk_workout_exercises_workout_days_workout_day_id",
                    column: x => x.workout_day_id,
                    principalTable: "workout_days",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "workout_feedback",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                workout_day_id = table.Column<Guid>(type: "uuid", nullable: false),
                completed = table.Column<bool>(type: "boolean", nullable: false),
                difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workout_feedback", x => x.id);
                table.CheckConstraint(
                    "ck_workout_feedback_difficulty",
                    "difficulty IN ('TooEasy', 'Appropriate', 'TooHard')");
                table.ForeignKey(
                    name: "fk_workout_feedback_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_workout_feedback_workout_days_workout_day_id",
                    column: x => x.workout_day_id,
                    principalTable: "workout_days",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_workout_plans_user_created_at",
            table: "workout_plans",
            columns: new[] { "user_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ux_workout_days_plan_date",
            table: "workout_days",
            columns: new[] { "workout_plan_id", "date" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_workout_recommendations_user_created_at",
            table: "workout_recommendations",
            columns: new[] { "user_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ix_workout_recommendations_workout_plan_id",
            table: "workout_recommendations",
            column: "workout_plan_id");
        migrationBuilder.CreateIndex(
            name: "ix_workout_completions_user_recorded_at",
            table: "workout_completions",
            columns: new[] { "user_id", "recorded_at" });
        migrationBuilder.CreateIndex(
            name: "ix_workout_completions_workout_day_id",
            table: "workout_completions",
            column: "workout_day_id");
        migrationBuilder.CreateIndex(
            name: "ux_workout_exercises_day_position",
            table: "workout_exercises",
            columns: new[] { "workout_day_id", "position" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_workout_feedback_user_created_at",
            table: "workout_feedback",
            columns: new[] { "user_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ix_workout_feedback_workout_day_id",
            table: "workout_feedback",
            column: "workout_day_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "workout_completions");
        migrationBuilder.DropTable(name: "workout_exercises");
        migrationBuilder.DropTable(name: "workout_feedback");
        migrationBuilder.DropTable(name: "workout_recommendations");
        migrationBuilder.DropTable(name: "workout_days");
        migrationBuilder.DropTable(name: "workout_plans");
    }
}
