using System.Net;
using System.Text;
using System.Text.Json;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.Infrastructure.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

/// <summary>
/// Free AI tiers (Gemini) answer 429/503 when busy: the client retries and then tries a second
/// model before giving up.
/// </summary>
public sealed class AIFallbackModelTests
{
    [Fact]
    public async Task ABusyPrimaryModelFallsBackToTheSecondModel()
    {
        var handler = new ModelHandler(busyModel: "primary");
        var client = Client(handler, fallback: "backup");

        var result = await client.CompleteAsync([new AIChatPromptMessage("user", "hola")], CancellationToken.None);

        Assert.Equal("backup", result.Model);
        Assert.Equal(["primary", "primary", "primary", "backup"], handler.Models);
    }

    [Fact]
    public async Task WithoutAFallbackTheBusyErrorIsReported()
    {
        var handler = new ModelHandler(busyModel: "primary");
        var client = Client(handler, fallback: null);

        var error = await Assert.ThrowsAsync<AIProviderException>(() =>
            client.CompleteAsync([new AIChatPromptMessage("user", "hola")], CancellationToken.None));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(3, handler.Models.Count);
    }

    [Fact]
    public async Task ARejectedRequestDoesNotTryTheFallback()
    {
        var handler = new ModelHandler(busyModel: null, rejectAll: true);
        var client = Client(handler, fallback: "backup");

        var error = await Assert.ThrowsAsync<AIProviderException>(() =>
            client.CompleteAsync([new AIChatPromptMessage("user", "hola")], CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(["primary"], handler.Models);
    }

    private static OpenAIClient Client(HttpMessageHandler handler, string? fallback) =>
        new(new HttpClient(handler), Options.Create(new AIOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            Model = "primary",
            FallbackModel = fallback,
            Endpoint = "https://example.test/v1/"
        }));

    private sealed class ModelHandler(string? busyModel, bool rejectAll = false) : HttpMessageHandler
    {
        public List<string> Models { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var model = body.RootElement.GetProperty("model").GetString()!;
            Models.Add(model);
            if (rejectAll)
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            if (model == busyModel)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var json = JsonSerializer.Serialize(new
            {
                model,
                choices = new[] { new { message = new { content = "ok" } } },
                usage = new { prompt_tokens = 3, completion_tokens = 1 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
