using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261018230000_AddGoalsAndNutrition")]
public sealed class AddGoalsAndNutrition : Migration
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
            name: "goals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                target_weight_kilograms = table.Column<double>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                target_date = table.Column<DateOnly>(type: "date", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goals", x => x.id);
                table.CheckConstraint(
                    "ck_goals_type",
                    "type IN ('FatLoss', 'MuscleGain', 'WeightMaintenance', 'Endurance', 'GeneralHealth')");
                table.CheckConstraint(
                    "ck_goals_status",
                    "status IN ('Active', 'Completed', 'Paused', 'Abandoned')");
                table.CheckConstraint(
                    "ck_goals_target_weight",
                    "target_weight_kilograms IS NULL OR target_weight_kilograms BETWEEN 20 AND 500");
                table.ForeignKey(
                    name: "fk_goals_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "nutrition_profiles",
            columns: table => new
            {
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                sex = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                activity_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_nutrition_profiles", x => x.user_id);
                table.CheckConstraint("ck_nutrition_profiles_sex", "sex IN ('Male', 'Female')");
                table.CheckConstraint(
                    "ck_nutrition_profiles_activity",
                    "activity_level IN ('Sedentary', 'Light', 'Moderate', 'High', 'Athlete')");
                table.ForeignKey(
                    name: "fk_nutrition_profiles_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "goal_progress",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                weight_kilograms = table.Column<double>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                completion_percent = table.Column<double>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                steps = table.Column<double>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                sleep_hours = table.Column<double>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goal_progress", x => x.id);
                table.CheckConstraint(
                    "ck_goal_progress_completion",
                    "completion_percent IS NULL OR completion_percent BETWEEN 0 AND 100");
                table.ForeignKey(
                    name: "fk_goal_progress_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "macro_targets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                calories = table.Column<double>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                protein_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                fat_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                carbohydrate_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_macro_targets", x => x.id);
                table.CheckConstraint(
                    "ck_macro_targets_amounts",
                    "calories >= 0 AND protein_grams >= 0 AND fat_grams >= 0 AND carbohydrate_grams >= 0");
                table.ForeignKey(
                    name: "fk_macro_targets_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_macro_targets_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "nutrition_logs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                meal_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                calories = table.Column<double>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                protein_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                fat_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                carbohydrate_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_nutrition_logs", x => x.id);
                table.CheckConstraint("ck_nutrition_logs_meal_type", "meal_type IN ('Breakfast', 'Lunch', 'Dinner', 'Snack')");
                table.CheckConstraint(
                    "ck_nutrition_logs_amounts",
                    "calories >= 0 AND protein_grams >= 0 AND fat_grams >= 0 AND carbohydrate_grams >= 0");
                table.ForeignKey(
                    name: "fk_nutrition_logs_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "daily_nutrition_summaries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                calories = table.Column<double>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                protein_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                fat_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                carbohydrate_grams = table.Column<double>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_daily_nutrition_summaries", x => x.id);
                table.CheckConstraint(
                    "ck_daily_nutrition_summaries_amounts",
                    "calories >= 0 AND protein_grams >= 0 AND fat_grams >= 0 AND carbohydrate_grams >= 0");
                table.ForeignKey(
                    name: "fk_daily_nutrition_summaries_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_goals_user_status_created_at",
            table: "goals",
            columns: new[] { "user_id", "status", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ux_goals_one_active_per_user",
            table: "goals",
            column: "user_id",
            unique: true,
            filter: "status = 'Active'");
        migrationBuilder.CreateIndex(
            name: "ux_goal_progress_goal_date",
            table: "goal_progress",
            columns: new[] { "goal_id", "date" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_macro_targets_user_effective_from",
            table: "macro_targets",
            columns: new[] { "user_id", "effective_from" });
        migrationBuilder.CreateIndex(
            name: "ix_macro_targets_goal_id",
            table: "macro_targets",
            column: "goal_id");
        migrationBuilder.CreateIndex(
            name: "ix_nutrition_logs_user_date_meal",
            table: "nutrition_logs",
            columns: new[] { "user_id", "date", "meal_type" });
        migrationBuilder.CreateIndex(
            name: "ux_daily_nutrition_summaries_user_date",
            table: "daily_nutrition_summaries",
            columns: new[] { "user_id", "date" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "daily_nutrition_summaries");
        migrationBuilder.DropTable(name: "goal_progress");
        migrationBuilder.DropTable(name: "macro_targets");
        migrationBuilder.DropTable(name: "nutrition_logs");
        migrationBuilder.DropTable(name: "nutrition_profiles");
        migrationBuilder.DropTable(name: "goals");
    }
}
