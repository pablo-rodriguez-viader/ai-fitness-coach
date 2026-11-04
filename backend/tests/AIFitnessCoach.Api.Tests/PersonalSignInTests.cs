using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIFitnessCoach.Domain.Entities;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static AIFitnessCoach.Api.Tests.BetaTestHost;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// Personal install with a free Apple ID (no Sign in with Apple): a secret access code opens the
/// owner's single account. Off unless Auth:PersonalAccessCode is configured.
/// </summary>
public sealed class PersonalSignInTests
{
    private const string Code = "personal-code-0123456789-abcdefghij";

    [Fact]
    public async Task PersonalSignInDoesNotExistUnlessConfigured()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/v1/auth/personal", new { code = Code });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheRightCodeAlwaysOpensTheSameAccountAndItWorksEndToEnd()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Auth:PersonalAccessCode"] = Code });

        var first = await SignInAsync(host, Code);
        var second = await SignInAsync(host, Code);

        Assert.Equal(first.UserId, second.UserId);
        using var profile = Authorized(HttpMethod.Put, "/api/v1/profile/me", first.AccessToken,
            new { displayName = "Pablo", age = 40, heightCentimeters = 178d, goal = "general_health" });
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(profile)).StatusCode);
        using var refresh = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profile/me");
        refresh.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(refresh)).StatusCode);
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            var identity = await db.UserIdentities.SingleAsync();
            Assert.Equal(IdentityProviders.Personal, identity.Provider);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong-code-0123456789-abcdefghijk")]
    [InlineData("personal-code-0123456789-abcdefghi")]
    public async Task AWrongCodeIsRejected(string code)
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Auth:PersonalAccessCode"] = Code });

        var response = await host.Client.PostAsJsonAsync("/api/v1/auth/personal", new { code });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = host.App.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().Users.AnyAsync());
    }

    [Fact]
    public async Task AShortCodeFailsAtStartup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsync(new Dictionary<string, string?> { ["Auth:PersonalAccessCode"] = "short" });
        });
    }

    [Fact]
    public async Task TheBetaCapStillApplies()
    {
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Auth:PersonalAccessCode"] = Code,
            ["Beta:MaxUsers"] = "1"
        });
        await host.SignInAsync("apple-someone-else");

        var response = await host.Client.PostAsJsonAsync("/api/v1/auth/personal", new { code = Code });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheOwnerCanStillSignInWhenTheCapIsReached()
    {
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Auth:PersonalAccessCode"] = Code,
            ["Beta:MaxUsers"] = "1"
        });
        var first = await SignInAsync(host, Code);

        var again = await SignInAsync(host, Code);

        Assert.Equal(first.UserId, again.UserId);
    }

    private static async Task<SignedInUser> SignInAsync(BetaTestHost host, string code)
    {
        var response = await host.Client.PostAsJsonAsync("/api/v1/auth/personal", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new SignedInUser(
            json.RootElement.GetProperty("userId").GetString()!,
            json.RootElement.GetProperty("accessToken").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }
}
