using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261027100000_AddSupportReports")]
public sealed class AddSupportReports : Migration
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
            name: "support_reports",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                rating = table.Column<int>(type: "integer", nullable: true),
                contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                app_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                build_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                os_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                device_model = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                diagnostics = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_support_reports", x => x.id);
                table.CheckConstraint("ck_support_reports_kind", "kind IN ('feedback', 'bug', 'support')");
                table.CheckConstraint("ck_support_reports_rating", "rating IS NULL OR rating BETWEEN 1 AND 5");
                table.ForeignKey(
                    name: "fk_support_reports_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_support_reports_user_created",
            table: "support_reports",
            columns: ["user_id", "created_at"]);
        migrationBuilder.CreateIndex(
            name: "ix_support_reports_kind_created",
            table: "support_reports",
            columns: ["kind", "created_at"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "support_reports");
    }
}
