using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261015100000_InitialHealthData")]
public sealed class InitialHealthData : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "9.0.0");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        InitialHealthDataModel.Configure(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_profiles",
            columns: table => new
            {
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                age = table.Column<int>(type: "integer", nullable: false),
                height_centimeters = table.Column<double>(type: "double precision", precision: 6, scale: 2, nullable: false),
                goal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_profiles", x => x.user_id);
                table.CheckConstraint("ck_user_profiles_age", "age BETWEEN 13 AND 120");
                table.CheckConstraint(
                    "ck_user_profiles_height",
                    "height_centimeters > 0 AND height_centimeters <= 300");
            });

        migrationBuilder.CreateTable(
            name: "health_metrics",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                metric = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                value = table.Column<double>(type: "double precision", precision: 16, scale: 4, nullable: false),
                unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_health_metrics", x => x.id);
                table.CheckConstraint("ck_health_metrics_value", "value >= 0");
                table.ForeignKey(
                    name: "fk_health_metrics_user_profiles_user_id",
                    column: x => x.user_id,
                    principalTable: "user_profiles",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_user_profiles_updated_at",
            table: "user_profiles",
            column: "updated_at");
        migrationBuilder.CreateIndex(
            name: "ix_health_metrics_user_id_metric_observed_at",
            table: "health_metrics",
            columns: new[] { "user_id", "metric", "observed_at" });
        migrationBuilder.CreateIndex(
            name: "ix_health_metrics_user_id_observed_at",
            table: "health_metrics",
            columns: new[] { "user_id", "observed_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "health_metrics");
        migrationBuilder.DropTable(name: "user_profiles");
    }
}

internal static class InitialHealthDataModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("AIFitnessCoach.Domain.Entities.HealthMetric", entity =>
        {
            entity.HasIndex("UserId", "Metric", "ObservedAt")
                .HasDatabaseName("ix_health_metrics_user_id_metric_observed_at");
            entity.HasIndex("UserId", "ObservedAt")
                .HasDatabaseName("ix_health_metrics_user_id_observed_at");
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property<string>("Metric").IsRequired().HasMaxLength(48).HasColumnType("character varying(48)").HasColumnName("metric");
            entity.Property<DateTimeOffset>("ObservedAt").HasColumnType("timestamp with time zone").HasColumnName("observed_at");
            entity.Property<string>("Unit").IsRequired().HasMaxLength(24).HasColumnType("character varying(24)").HasColumnName("unit");
            entity.Property<string>("UserId").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("user_id");
            entity.Property<double>("Value").HasPrecision(16, 4).HasColumnType("double precision").HasColumnName("value");
            entity.HasKey("Id").HasName("pk_health_metrics");
            entity.ToTable("health_metrics", table =>
            {
                table.HasCheckConstraint("ck_health_metrics_value", "value >= 0");
            });
        });
        modelBuilder.Entity("AIFitnessCoach.Domain.Entities.UserProfile", entity =>
        {
            entity.HasIndex("UpdatedAt").HasDatabaseName("ix_user_profiles_updated_at");
            entity.Property<string>("UserId").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("user_id");
            entity.Property<int>("Age").HasColumnType("integer").HasColumnName("age");
            entity.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property<string>("DisplayName").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("display_name");
            entity.Property<string>("Goal").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("goal");
            entity.Property<double>("HeightCentimeters").HasPrecision(6, 2).HasColumnType("double precision").HasColumnName("height_centimeters");
            entity.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            entity.HasKey("UserId").HasName("pk_user_profiles");
            entity.ToTable("user_profiles", table =>
            {
                table.HasCheckConstraint("ck_user_profiles_age", "age BETWEEN 13 AND 120");
                table.HasCheckConstraint(
                    "ck_user_profiles_height",
                    "height_centimeters > 0 AND height_centimeters <= 300");
            });
        });
        modelBuilder.Entity("AIFitnessCoach.Domain.Entities.HealthMetric", entity =>
        {
            entity.HasOne("AIFitnessCoach.Domain.Entities.UserProfile", "UserProfile")
                .WithMany("HealthMetrics")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("UserProfile");
        });
        modelBuilder.Entity("AIFitnessCoach.Domain.Entities.UserProfile", entity =>
        {
            entity.Navigation("HealthMetrics");
        });
    }
}
