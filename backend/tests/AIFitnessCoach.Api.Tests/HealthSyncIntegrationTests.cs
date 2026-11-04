using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Api.Endpoints;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.UseCases.HealthMetrics;
using AIFitnessCoach.Application.UseCases.Authentication;
using AIFitnessCoach.Application.UseCases.Profiles;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using AIFitnessCoach.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class HealthSyncIntegrationTests
{
    [Fact]
    public async Task BatchEndpointAuthenticatesScopesUpsertsAndDeletesSamples()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<FitnessDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<IHealthMetricRepository, HealthMetricRepository>();
        builder.Services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        builder.Services.AddScoped<IUserConsentStore, UserConsentStore>();
        builder.Services.AddScoped<SyncHealthMetricsUseCase>();
        builder.Services.AddScoped<DeleteHealthMetricsUseCase>();
        builder.Services.AddScoped<GetHealthMetricsUseCase>();
        builder.Services.AddScoped<GetLatestHealthMetricsUseCase>();
        builder.Services.AddScoped<GetHealthSummaryUseCase>();
        builder.Services.AddScoped<CreateHealthMetricUseCase>();
        builder.Services.AddScoped<CreateUserProfileUseCase>();
        builder.Services.AddScoped<GetUserProfileUseCase>();
        builder.Services.AddScoped<AIFitnessCoach.Application.UseCases.Authentication.AuthenticationService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            dbContext.Users.Add(User.Create("user-1", "Ada", DateTimeOffset.UtcNow));
            dbContext.Users.Add(User.Create("user-2", "Grace", DateTimeOffset.UtcNow));
            // Uploads require the explicit health-data consent.
            dbContext.UserConsents.Add(UserConsent.Create("user-1", ConsentKinds.HealthData, true, 1, DateTimeOffset.UtcNow));
            dbContext.UserConsents.Add(UserConsent.Create("user-2", ConsentKinds.HealthData, true, 1, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapApiEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<
                    Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var profileBeforeSetup = AuthenticatedRequest(
                HttpMethod.Get, "/api/v1/profile/me", "user-1");
            var missingProfile = await client.SendAsync(profileBeforeSetup);
            Assert.Equal(HttpStatusCode.NotFound, missingProfile.StatusCode);

            using var saveProfile = AuthenticatedRequest(
                HttpMethod.Put,
                "/api/v1/profile/me",
                "user-1",
                new { displayName = "Ada", age = 35, heightCentimeters = 168d, goal = "general_health" });
            var savedProfile = await client.SendAsync(saveProfile);
            savedProfile.EnsureSuccessStatusCode();
            using var savedProfileJson = JsonDocument.Parse(await savedProfile.Content.ReadAsStringAsync());
            Assert.Equal(35, savedProfileJson.RootElement.GetProperty("age").GetInt32());

            using var fetchProfile = AuthenticatedRequest(
                HttpMethod.Get, "/api/v1/profile/me", "user-1");
            var loadedProfile = await client.SendAsync(fetchProfile);
            loadedProfile.EnsureSuccessStatusCode();
            using var loadedProfileJson = JsonDocument.Parse(await loadedProfile.Content.ReadAsStringAsync());
            Assert.Equal("Ada", loadedProfileJson.RootElement.GetProperty("displayName").GetString());

            var unauthenticated = await client.GetAsync("/api/v1/health-metrics/latest");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            var unauthenticatedAI = await client.GetAsync("/api/v1/ai/conversations");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedAI.StatusCode);
            var unauthenticatedAnalysis = await client.PostAsJsonAsync(
                "/api/v1/ai/health-analysis",
                new { question = "¿Cómo va mi actividad?", period = "last7Days" });
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedAnalysis.StatusCode);
            var unauthenticatedUsage = await client.GetAsync("/api/v1/ai/usage/summary");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedUsage.StatusCode);
            var unauthenticatedConsent = await client.DeleteAsync("/api/v1/ai/consent");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedConsent.StatusCode);
            var unauthenticatedNutritionProfile = await client.GetAsync("/api/v1/nutrition/profile");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedNutritionProfile.StatusCode);
            var unauthenticatedNutritionTargets = await client.GetAsync("/api/v1/nutrition/targets");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedNutritionTargets.StatusCode);
            var unauthenticatedNutritionProgress = await client.GetAsync("/api/v1/nutrition/progress");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedNutritionProgress.StatusCode);
            var unauthenticatedNutritionGoal = await client.PostAsJsonAsync(
                "/api/v1/nutrition/goals",
                new { type = "fat_loss", targetWeightKilograms = 70 });
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedNutritionGoal.StatusCode);
            var unauthenticatedCoach = await client.PostAsJsonAsync(
                "/api/v1/ai/coach",
                new { question = "How was my week?" });
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedCoach.StatusCode);

            var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
            var sample = new
            {
                userId = "user-1",
                metrics = new[]
                {
                    new
                    {
                        metric = "steps",
                        value = 250d,
                        unit = "count",
                        observedAt = timestamp,
                        source = "apple_health",
                        externalId = "hk-sample-1",
                        deviceId = "device-1"
                    },
                    new
                    {
                        metric = "active_energy",
                        value = 350d,
                        unit = "kcal",
                        observedAt = timestamp,
                        source = "apple_health",
                        externalId = "hk-sample-2",
                        deviceId = "device-1"
                    },
                    new
                    {
                        metric = "basal_energy",
                        value = 1200d,
                        unit = "kcal",
                        observedAt = timestamp,
                        source = "apple_health",
                        externalId = "hk-sample-3",
                        deviceId = "device-1"
                    }
                }
            };

            using var ownerRequest = AuthenticatedRequest(
                HttpMethod.Post,
                "/api/v1/health-metrics/batch",
                "user-1",
                sample);
            var created = await client.SendAsync(ownerRequest);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal(3, createdJson.RootElement.GetProperty("insertedCount").GetInt32());

            using var duplicateRequest = AuthenticatedRequest(
                HttpMethod.Post,
                "/api/v1/health-metrics/batch",
                "user-1",
                sample);
            var duplicate = await client.SendAsync(duplicateRequest);
            using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
            Assert.Equal(3, duplicateJson.RootElement.GetProperty("duplicateCount").GetInt32());

            using var wrongOwnerRequest = AuthenticatedRequest(
                HttpMethod.Post,
                "/api/v1/health-metrics/batch",
                "user-1",
                new
                {
                    userId = "user-2",
                    metrics = sample.metrics
                });
            var forbidden = await client.SendAsync(wrongOwnerRequest);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            using var summaryRequest = AuthenticatedRequest(
                HttpMethod.Get,
                "/api/v1/health-summary?from="
                    + Uri.EscapeDataString(timestamp.AddMinutes(-1).ToString("O"))
                    + "&to="
                    + Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O")),
                "user-1");
            var summaryResponse = await client.SendAsync(summaryRequest);
            summaryResponse.EnsureSuccessStatusCode();
            using var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
            Assert.Equal(250, summaryJson.RootElement.GetProperty("steps").GetDouble());
            Assert.Equal(1550, summaryJson.RootElement.GetProperty("totalEnergyKilocalories").GetDouble());

            using var deleteRequest = AuthenticatedRequest(
                HttpMethod.Post,
                "/api/v1/health-metrics/batch",
                "user-1",
                new
                {
                    userId = "user-1",
                    deletedMetrics = new[]
                    {
                        new { source = "apple_health", externalId = "hk-sample-1", deviceId = "device-1" }
                    }
                });
            var deleted = await client.SendAsync(deleteRequest);
            deleted.EnsureSuccessStatusCode();
            using var deletedJson = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync());
            Assert.Equal(1, deletedJson.RootElement.GetProperty("deletedCount").GetInt32());

            using var latestRequest = AuthenticatedRequest(
                HttpMethod.Get,
                "/api/v1/health-metrics/latest",
                "user-2");
            var otherUserLatest = await client.SendAsync(latestRequest);
            otherUserLatest.EnsureSuccessStatusCode();
            var otherUserMetrics = await otherUserLatest.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
            Assert.Empty(otherUserMetrics);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static HttpRequestMessage AuthenticatedRequest(
        HttpMethod method,
        string path,
        string userId,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-User", userId);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrWhiteSpace(userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [new Claim("sub", userId)],
                Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
