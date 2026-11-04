using AIFitnessCoach.Api;
using AIFitnessCoach.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApiServices(
    builder.Configuration);

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyDevelopmentMigrationsAsync();
}

app.UseApiPipeline();
app.Run();

public partial class Program;
