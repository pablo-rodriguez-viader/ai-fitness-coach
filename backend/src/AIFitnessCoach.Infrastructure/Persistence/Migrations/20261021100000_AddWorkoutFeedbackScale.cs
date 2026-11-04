using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIFitnessCoach.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261021100000_AddWorkoutFeedbackScale")]
public sealed class AddWorkoutFeedbackScale : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        FitnessDbContextModelConfiguration.Configure(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_workout_feedback_difficulty",
            table: "workout_feedback");
        migrationBuilder.AddCheckConstraint(
            name: "ck_workout_feedback_difficulty",
            table: "workout_feedback",
            sql: "difficulty IN ('TooEasy', 'Appropriate', 'TooHard', 'VeryEasy', 'Easy', 'Hard', 'VeryHard')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE workout_feedback SET difficulty = 'TooEasy' WHERE difficulty IN ('VeryEasy', 'Easy')");
        migrationBuilder.Sql("UPDATE workout_feedback SET difficulty = 'TooHard' WHERE difficulty IN ('Hard', 'VeryHard')");
        migrationBuilder.DropCheckConstraint(
            name: "ck_workout_feedback_difficulty",
            table: "workout_feedback");
        migrationBuilder.AddCheckConstraint(
            name: "ck_workout_feedback_difficulty",
            table: "workout_feedback",
            sql: "difficulty IN ('TooEasy', 'Appropriate', 'TooHard')");
    }
}
