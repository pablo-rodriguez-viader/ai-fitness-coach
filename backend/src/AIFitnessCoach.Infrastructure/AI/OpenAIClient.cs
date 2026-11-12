using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIFitnessCoach.Infrastructure.AI;

public sealed class OpenAIClient(
    HttpClient httpClient,
    IOptions<AIOptions> options,
    ILogger<OpenAIClient>? logger = null) : IOpenAIClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AIChatCompletion> CompleteAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        CancellationToken cancellationToken) =>
        await CompleteCoreAsync(messages, jsonMode: false, cancellationToken);

    public async Task<AIChatCompletion> CompleteJsonAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        CancellationToken cancellationToken) =>
        await CompleteCoreAsync(messages, jsonMode: true, cancellationToken);

    private async Task<AIChatCompletion> CompleteCoreAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        try
        {
            return await CompleteWithModelAsync(messages, jsonMode, configuration.Model, cancellationToken);
        }
        catch (AIProviderException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(
                "AI provider failed with model {Model}: {Reason} (HTTP {Status}).",
                configuration.Model, exception.Message, exception.StatusCode);
            var fallback = configuration.FallbackModel?.Trim();
            // Free tiers (Gemini) answer 429 or 503 when busy; a second model usually still has room.
            if (string.IsNullOrEmpty(fallback)
                || string.Equals(fallback, configuration.Model, StringComparison.Ordinal)
                || exception.StatusCode is not (429 or >= 500))
                throw;
            try
            {
                return await CompleteWithModelAsync(messages, jsonMode, fallback, cancellationToken);
            }
            catch (AIProviderException fallbackException) when (!cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(
                    "AI fallback model {Model} also failed: {Reason} (HTTP {Status}).",
                    fallback, fallbackException.Message, fallbackException.StatusCode);
                throw;
            }
        }
    }

    private async Task<AIChatCompletion> CompleteWithModelAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        bool jsonMode,
        string model,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.ApiKey))
            throw new AIProviderException("AI provider is not configured.");

        var baseAddress = configuration.Endpoint.EndsWith("/", StringComparison.Ordinal)
            ? configuration.Endpoint
            : configuration.Endpoint + "/";
        var endpoint = new Uri(new Uri(baseAddress, UriKind.Absolute), "chat/completions");
        var body = JsonSerializer.Serialize(new OpenAIRequest(
            model,
            messages,
            configuration.MaxOutputTokens,
            jsonMode ? new OpenAIResponseFormat("json_object") : null), JsonOptions);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var response = await SendWithRetryAsync(endpoint, configuration.ApiKey, body, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new AIProviderException(
                    $"AI provider returned HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode);
            if (response.Content.Headers.ContentLength is > 1_048_576)
                throw new AIProviderException("AI provider response exceeded the allowed size.");
            try
            {
                await response.Content.LoadIntoBufferAsync(1_048_576, timeout.Token);
            }
            catch (InvalidOperationException)
            {
                throw new AIProviderException("AI provider response exceeded the allowed size.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var payload = await JsonSerializer.DeserializeAsync<OpenAIResponse>(
                stream,
                JsonOptions,
                timeout.Token);
            var choice = payload?.Choices?.FirstOrDefault();
            if (choice?.Message?.Content is not { Length: > 0 } content
                || payload?.Usage is null
                || payload.Usage.PromptTokens < 0
                || payload.Usage.CompletionTokens < 0)
            {
                throw new AIProviderException("AI provider returned an invalid completion response.");
            }
            return new AIChatCompletion(
                content,
                payload.Model ?? model,
                payload.Usage.PromptTokens,
                payload.Usage.CompletionTokens);
        }
        catch (JsonException exception)
        {
            throw new AIProviderException($"AI provider returned invalid JSON ({exception.GetType().Name}).");
        }
        catch (HttpRequestException)
        {
            throw new AIProviderException("AI provider response could not be read.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AIProviderException("AI provider request timed out.");
        }
    }

    private const int MaximumAttempts = 3;

    /// <summary>
    /// Sends the completion request, retrying twice on rate limiting, provider 5xx or a failed
    /// connection. Everything shares the caller's 60-second budget.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Uri endpoint,
        string apiKey,
        string body,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaximumAttempts)
            {
                await Task.Delay(RetryDelay(attempt, null), cancellationToken);
                continue;
            }
            catch (HttpRequestException)
            {
                throw new AIProviderException("AI provider could not be reached.");
            }

            if (attempt < MaximumAttempts && IsRetryable((int)response.StatusCode))
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await Task.Delay(RetryDelay(attempt, retryAfter), cancellationToken);
                continue;
            }
            return response;
        }
    }

    private static bool IsRetryable(int statusCode) =>
        statusCode is 429 or 500 or 502 or 503 or 504;

    private static TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter) =>
        retryAfter is { } delay && delay >= TimeSpan.Zero && delay <= TimeSpan.FromSeconds(10)
            ? delay
            : TimeSpan.FromMilliseconds(1500 * attempt);

    private sealed record OpenAIRequest(
        string Model,
        IReadOnlyList<AIChatPromptMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        OpenAIResponseFormat? ResponseFormat);

    private sealed record OpenAIResponseFormat(string Type);

    private sealed record OpenAIResponse(
        string? Model,
        IReadOnlyList<OpenAIChoice>? Choices,
        OpenAIUsage? Usage);

    private sealed record OpenAIChoice(OpenAIMessage? Message);
    private sealed record OpenAIMessage(string? Content);

    private sealed record OpenAIUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
