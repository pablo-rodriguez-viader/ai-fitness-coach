using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AIFitnessCoach.Api;
using AIFitnessCoach.Api.Configuration;
using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// Runs the API with the production composition root and pipeline. Only PostgreSQL (replaced by
/// EF InMemory) and Apple's token validation (replaced by a fake) differ from a real deployment.
/// </summary>
internal sealed class BetaTestHost : IAsyncDisposable
{
    private BetaTestHost(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
    }

    public WebApplication App { get; }
    public HttpClient Client { get; }

    public static async Task<BetaTestHost> StartAsync(
        IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused-in-tests",
            ["Jwt:Issuer"] = "https://tests.aifitnesscoach.example",
            ["Jwt:Audience"] = "aifitnesscoach-tests",
            ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Apple:Audience"] = "com.example.aifitnesscoach.tests",
            ["AI:Enabled"] = "false"
        };
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
                configuration[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(configuration);

        builder.Services.AddApiServices(builder.Configuration);
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.RemoveAll<DbContextOptions<FitnessDbContext>>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<FitnessDbContext>>();
        builder.Services.AddDbContext<FitnessDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.RemoveAll<IAppleAuthenticationService>();
        builder.Services.AddSingleton<IAppleAuthenticationService, FakeAppleTokens>();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseApiPipeline();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        return new BetaTestHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    public async Task<SignedInUser> SignInAsync(string appleSubject = "apple-user-1", bool grantHealthConsent = true)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/apple",
            new { identityToken = appleSubject, nonce = "test-nonce", displayName = "Beta Tester" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var user = new SignedInUser(
            json.RootElement.GetProperty("userId").GetString()!,
            json.RootElement.GetProperty("accessToken").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
        if (grantHealthConsent)
        {
            // Like the app: the explicit health-data consent comes before connecting Apple Health.
            using var consent = Authorized(HttpMethod.Put, "/api/v1/consents/health_data", user.AccessToken,
                new { granted = true, version = 1 });
            (await Client.SendAsync(consent)).EnsureSuccessStatusCode();
        }
        return user;
    }

    public static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string accessToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }

    internal sealed record SignedInUser(string UserId, string AccessToken, string RefreshToken);

    /// <summary>Treats the identity token as the Apple subject; signature checks are covered elsewhere.</summary>
    private sealed class FakeAppleTokens : IAppleAuthenticationService
    {
        public Task<AppleIdentity> ValidateIdentityTokenAsync(
            string identityToken,
            string rawNonce,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AppleIdentity(identityToken, null, false));
    }
}
