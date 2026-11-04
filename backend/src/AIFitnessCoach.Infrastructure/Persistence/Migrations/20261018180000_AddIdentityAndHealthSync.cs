using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261018180000_AddIdentityAndHealthSync")]
public sealed class AddIdentityAndHealthSync : Migration
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
            name: "users",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_users", x => x.id));

        migrationBuilder.Sql("""
            INSERT INTO users (id, display_name, created_at)
            SELECT user_id, display_name, created_at
            FROM user_profiles
            ON CONFLICT (id) DO NOTHING;
            """);

        migrationBuilder.CreateTable(
            name: "auth_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_sessions", x => x.id);
                table.CheckConstraint("ck_auth_sessions_expiry", "expires_at > created_at");
                table.ForeignKey("fk_auth_sessions_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_identities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                provider_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                email_verified = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_identities", x => x.id);
                table.CheckConstraint("ck_user_identities_provider", "provider = 'apple'");
                table.ForeignKey("fk_user_identities_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                auth_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                replaced_by_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_refresh_tokens", x => x.id);
                table.CheckConstraint("ck_refresh_tokens_expiry", "expires_at > created_at");
                table.ForeignKey("fk_refresh_tokens_auth_sessions_session_id", x => x.auth_session_id, "auth_sessions", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<string>(
            name: "source",
            table: "health_metrics",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "source_record_id",
            table: "health_metrics",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE health_metrics
            SET source = 'legacy',
                source_record_id = id::text;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "source",
            table: "health_metrics",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64,
            oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "source_record_id",
            table: "health_metrics",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.DropForeignKey(
            name: "fk_health_metrics_user_profiles_user_id",
            table: "health_metrics");
        migrationBuilder.AddForeignKey(
            name: "fk_health_metrics_users_user_id",
            table: "health_metrics",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey(
            name: "fk_user_profiles_users_user_id",
            table: "user_profiles",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.CreateIndex(
            name: "ix_auth_sessions_user_active",
            table: "auth_sessions",
            columns: new[] { "user_id", "revoked_at", "expires_at" });
        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_session_revoked",
            table: "refresh_tokens",
            columns: new[] { "auth_session_id", "revoked_at" });
        migrationBuilder.CreateIndex(
            name: "ux_refresh_tokens_token_hash",
            table: "refresh_tokens",
            column: "token_hash",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_user_identities_user_id",
            table: "user_identities",
            column: "user_id");
        migrationBuilder.CreateIndex(
            name: "ux_user_identities_provider_subject",
            table: "user_identities",
            columns: new[] { "provider", "provider_subject" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_users_created_at",
            table: "users",
            column: "created_at");
        migrationBuilder.CreateIndex(
            name: "ux_health_metrics_user_source_record",
            table: "health_metrics",
            columns: new[] { "user_id", "source", "source_record_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("fk_health_metrics_users_user_id", "health_metrics");
        migrationBuilder.DropForeignKey("fk_user_profiles_users_user_id", "user_profiles");
        migrationBuilder.DropIndex("ux_health_metrics_user_source_record", "health_metrics");
        migrationBuilder.DropColumn("source", "health_metrics");
        migrationBuilder.DropColumn("source_record_id", "health_metrics");
        migrationBuilder.AddForeignKey(
            name: "fk_health_metrics_user_profiles_user_id",
            table: "health_metrics",
            column: "user_id",
            principalTable: "user_profiles",
            principalColumn: "user_id",
            onDelete: ReferentialAction.Cascade);
        migrationBuilder.DropTable("refresh_tokens");
        migrationBuilder.DropTable("user_identities");
        migrationBuilder.DropTable("auth_sessions");
        migrationBuilder.DropTable("users");
    }
}
