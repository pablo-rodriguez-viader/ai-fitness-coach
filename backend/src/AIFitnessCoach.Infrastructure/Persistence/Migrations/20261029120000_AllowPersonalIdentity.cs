using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

/// <summary>Allows the "personal" identity provider (personal install without Sign in with Apple).</summary>
[DbContext(typeof(FitnessDbContext))]
[Migration("20261029120000_AllowPersonalIdentity")]
public sealed class AllowPersonalIdentity : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_user_identities_provider", table: "user_identities");
        migrationBuilder.AddCheckConstraint(
            name: "ck_user_identities_provider",
            table: "user_identities",
            sql: "provider IN ('apple', 'personal')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM users WHERE id IN (SELECT user_id FROM user_identities WHERE provider = 'personal');");
        migrationBuilder.DropCheckConstraint(name: "ck_user_identities_provider", table: "user_identities");
        migrationBuilder.AddCheckConstraint(
            name: "ck_user_identities_provider",
            table: "user_identities",
            sql: "provider = 'apple'");
    }
}
