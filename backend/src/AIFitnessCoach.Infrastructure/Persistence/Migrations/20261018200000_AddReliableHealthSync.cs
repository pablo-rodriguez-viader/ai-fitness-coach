using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261018200000_AddReliableHealthSync")]
public sealed class AddReliableHealthSync : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_health_metrics_user_source_record",
            table: "health_metrics");

        migrationBuilder.RenameColumn(
            name: "source_record_id",
            table: "health_metrics",
            newName: "external_id");

        migrationBuilder.AddColumn<string>(
            name: "device_id",
            table: "health_metrics",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "legacy-device");
        migrationBuilder.AddColumn<int>(
            name: "sync_version",
            table: "health_metrics",
            type: "integer",
            nullable: false,
            defaultValue: 1);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "updated_at",
            table: "health_metrics",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.Sql("UPDATE health_metrics SET updated_at = created_at;");

        migrationBuilder.CreateIndex(
            name: "ux_health_metrics_user_source_record",
            table: "health_metrics",
            columns: new[] { "user_id", "source", "external_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ux_health_metrics_user_source_record", "health_metrics");
        migrationBuilder.DropColumn("device_id", "health_metrics");
        migrationBuilder.DropColumn("sync_version", "health_metrics");
        migrationBuilder.DropColumn("updated_at", "health_metrics");
        migrationBuilder.RenameColumn(
            name: "external_id",
            table: "health_metrics",
            newName: "source_record_id");
        migrationBuilder.CreateIndex(
            name: "ux_health_metrics_user_source_record",
            table: "health_metrics",
            columns: new[] { "user_id", "source", "source_record_id" },
            unique: true);
    }
}
