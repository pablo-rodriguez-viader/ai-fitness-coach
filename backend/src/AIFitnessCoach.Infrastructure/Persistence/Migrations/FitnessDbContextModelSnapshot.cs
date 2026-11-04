using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
public sealed class FitnessDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }
}
