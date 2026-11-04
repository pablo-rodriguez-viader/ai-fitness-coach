using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261018210000_AddAIPlatform")]
public sealed class AddAIPlatform : Migration
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
            name: "ai_conversations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                context_version = table.Column<int>(type: "integer", nullable: false),
                input_tokens = table.Column<int>(type: "integer", nullable: false),
                output_tokens = table.Column<int>(type: "integer", nullable: false),
                estimated_cost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_conversations", x => x.id);
                table.CheckConstraint(
                    "ck_ai_conversations_estimated_cost",
                    "estimated_cost >= 0");
                table.CheckConstraint(
                    "ck_ai_conversations_token_counts",
                    "input_tokens >= 0 AND output_tokens >= 0");
                table.ForeignKey(
                    name: "fk_ai_conversations_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_messages", x => x.id);
                table.CheckConstraint("ck_ai_messages_role", "role IN ('User', 'Assistant')");
                table.ForeignKey(
                    name: "fk_ai_messages_ai_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "ai_conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_context_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                context_version = table.Column<int>(type: "integer", nullable: false),
                context_json = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_context_snapshots", x => x.id);
                table.ForeignKey(
                    name: "fk_ai_context_snapshots_ai_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "ai_conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_ai_context_snapshots_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_token_usage",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                input_tokens = table.Column<int>(type: "integer", nullable: false),
                output_tokens = table.Column<int>(type: "integer", nullable: false),
                estimated_cost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ai_token_usage", x => x.id);
                table.CheckConstraint("ck_ai_token_usage_estimated_cost", "estimated_cost >= 0");
                table.CheckConstraint(
                    "ck_ai_token_usage_counts",
                    "input_tokens >= 0 AND output_tokens >= 0");
                table.ForeignKey(
                    name: "fk_ai_token_usage_ai_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "ai_conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_ai_token_usage_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ai_conversations_user_updated_at",
            table: "ai_conversations",
            columns: new[] { "user_id", "updated_at" });
        migrationBuilder.CreateIndex(
            name: "ix_ai_messages_conversation_created_at",
            table: "ai_messages",
            columns: new[] { "conversation_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ix_ai_context_snapshots_conversation_created_at",
            table: "ai_context_snapshots",
            columns: new[] { "conversation_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ix_ai_context_snapshots_user_id",
            table: "ai_context_snapshots",
            column: "user_id");
        migrationBuilder.CreateIndex(
            name: "ix_ai_token_usage_conversation_created_at",
            table: "ai_token_usage",
            columns: new[] { "conversation_id", "created_at" });
        migrationBuilder.CreateIndex(
            name: "ix_ai_token_usage_user_created_at",
            table: "ai_token_usage",
            columns: new[] { "user_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ai_context_snapshots");
        migrationBuilder.DropTable(name: "ai_messages");
        migrationBuilder.DropTable(name: "ai_token_usage");
        migrationBuilder.DropTable(name: "ai_conversations");
    }
}
