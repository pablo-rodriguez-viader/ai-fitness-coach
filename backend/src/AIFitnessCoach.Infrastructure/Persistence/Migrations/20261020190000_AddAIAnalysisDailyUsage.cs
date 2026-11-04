using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261020190000_AddAIAnalysisDailyUsage")]
public sealed class AddAIAnalysisDailyUsage : Migration
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
            name: "ai_analysis_daily_usage",
            columns: table => new
            {
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                request_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_analysis_daily_usage", x => new { x.user_id, x.usage_date });
                table.CheckConstraint("ck_ai_analysis_daily_usage_request_count", "request_count BETWEEN 1 AND 200");
                table.ForeignKey(
                    name: "fk_ai_analysis_daily_usage_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ai_analysis_daily_usage");
}
