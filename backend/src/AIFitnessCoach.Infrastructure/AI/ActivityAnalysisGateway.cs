using System.Diagnostics.Metrics;
using System.Diagnostics;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using AIFitnessCoach.Application.UseCases.AI;
using Microsoft.Extensions.Logging;

namespace AIFitnessCoach.Infrastructure.AI;

public sealed class ActivityAnalysisGateway(
    IOpenAIClient openAIClient,
    ILogger<ActivityAnalysisGateway> logger) : IActivityAnalysisGateway
{
    private static readonly Meter Meter = new("AIFitnessCoach.ActivityAnalysis");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("ai.activity_analysis.requests");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>("ai.activity_analysis.input_tokens");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>("ai.activity_analysis.output_tokens");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("ai.activity_analysis.failures");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("ai_request_duration", "ms");
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>("ai_requests_total");
    private static readonly Counter<long> FailedRequestsTotal = Meter.CreateCounter<long>("ai_requests_failed_total");
    private static readonly Counter<long> InputTokensTotal = Meter.CreateCounter<long>("ai_input_tokens_total");
    private static readonly Counter<long> OutputTokensTotal = Meter.CreateCounter<long>("ai_output_tokens_total");
    private static readonly Counter<long> ProviderTimeouts = Meter.CreateCounter<long>("ai_provider_timeouts_total");

    public async Task<ActivityAnalysisProviderResult> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var completion = await openAIClient.CompleteJsonAsync(
                [
                    new AIChatPromptMessage("system", systemPrompt),
                    new AIChatPromptMessage("user", userPrompt)
                ],
                cancellationToken);
            Requests.Add(1);
            RequestsTotal.Add(1);
            InputTokens.Add(completion.InputTokens);
            OutputTokens.Add(completion.OutputTokens);
            InputTokensTotal.Add(completion.InputTokens);
            OutputTokensTotal.Add(completion.OutputTokens);
            RequestDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            logger.LogInformation(
                "AI activity analysis provider completed. Model {Model}, input tokens {InputTokens}, output tokens {OutputTokens}, duration ms {DurationMilliseconds}.",
                completion.Model,
                completion.InputTokens,
                completion.OutputTokens,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new ActivityAnalysisProviderResult(
                completion.Content,
                completion.Model,
                completion.InputTokens,
                completion.OutputTokens);
        }
        catch (AIProviderException exception)
        {
            Failures.Add(1, new KeyValuePair<string, object?>("status_code", exception.StatusCode ?? 0));
            FailedRequestsTotal.Add(1);
            RequestDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            logger.LogWarning(
                "AI activity analysis provider failed with status code {StatusCode}.",
                exception.StatusCode);
            throw new ActivityAnalysisProviderException("AI analysis is temporarily unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ProviderTimeouts.Add(1);
            FailedRequestsTotal.Add(1);
            RequestDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            logger.LogWarning("AI activity analysis provider timed out.");
            throw new ActivityAnalysisProviderException("AI analysis timed out. Please try again.");
        }
    }
}
