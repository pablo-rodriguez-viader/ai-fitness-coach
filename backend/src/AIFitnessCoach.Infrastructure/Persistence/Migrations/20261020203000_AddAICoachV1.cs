using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261020203000_AddAICoachV1")]
public sealed class AddAICoachV1 : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "token_count",
            table: "ai_analysis_daily_usage",
            type: "integer",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddCheckConstraint(
            name: "ck_ai_analysis_daily_usage_token_count",
            table: "ai_analysis_daily_usage",
            sql: "token_count >= 0");

        migrationBuilder.CreateTable(
            name: "ai_consents",
            columns: table => new
            {
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                granted = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_consents", x => x.user_id);
                table.ForeignKey(
                    name: "fk_ai_consents_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_coach_requests",
            columns: table => new
            {
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                response_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_coach_requests", x => new { x.user_id, x.client_request_id });
                table.CheckConstraint("ck_ai_coach_requests_status", "status IN ('Processing', 'Completed')");
                table.ForeignKey(
                    name: "fk_ai_coach_requests_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_ai_coach_requests_ai_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "ai_conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_conversation_summaries",
            columns: table => new
            {
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                summary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                context_version = table.Column<int>(type: "integer", nullable: false),
                prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                message_count_included = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_conversation_summaries", x => x.conversation_id);
                table.ForeignKey(
                    name: "fk_ai_conversation_summaries_ai_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "ai_conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_feedback",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                rating = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_feedback", x => x.id);
                table.CheckConstraint("ck_ai_feedback_rating", "rating IN ('Helpful', 'NotHelpful')");
                table.ForeignKey(
                    name: "fk_ai_feedback_ai_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "ai_conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_ai_feedback_ai_messages_message_id",
                    column: x => x.message_id,
                    principalTable: "ai_messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_ai_feedback_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ai_coach_requests_created_at",
            table: "ai_coach_requests",
            column: "created_at");
        migrationBuilder.CreateIndex(
            name: "ix_ai_coach_requests_conversation_id",
            table: "ai_coach_requests",
            column: "conversation_id");
        migrationBuilder.CreateIndex(
            name: "ix_ai_conversation_summaries_updated_at",
            table: "ai_conversation_summaries",
            column: "updated_at");
        migrationBuilder.CreateIndex(
            name: "ix_ai_feedback_user_created_at",
            table: "ai_feedback",
            columns: new[] { "user_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ux_ai_feedback_user_message",
            table: "ai_feedback",
            columns: new[] { "user_id", "message_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ai_coach_requests");
        migrationBuilder.DropTable(name: "ai_consents");
        migrationBuilder.DropTable(name: "ai_conversation_summaries");
        migrationBuilder.DropTable(name: "ai_feedback");
        migrationBuilder.DropCheckConstraint(
            name: "ck_ai_analysis_daily_usage_token_count",
            table: "ai_analysis_daily_usage");
        migrationBuilder.DropColumn(name: "token_count", table: "ai_analysis_daily_usage");
    }
}
