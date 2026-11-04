using System.Diagnostics.Metrics;
using AIFitnessCoach.AI.Abstractions;
using AIFitnessCoach.AI.Configuration;
using AIFitnessCoach.AI.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIFitnessCoach.Infrastructure.AI;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Stops calling the AI provider after repeated outages so users get a fast 503 instead of
/// waiting up to 60 s per request, and the provider is not hammered while it recovers.
/// Closed → Open after <see cref="FailureThreshold"/> consecutive provider failures (5xx, 429,
/// timeouts, connection errors). After <see cref="BreakDuration"/> one trial request is let
/// through (half-open): success closes the circuit, failure opens it again.
/// Client errors (4xx other than 429) never open it: they mean a bad request, not an outage.
/// One instance per process (singleton).
/// </summary>
public sealed class AICircuitBreaker
{
    private static readonly Meter Meter = new("AIFitnessCoach.AIProvider");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("ai_circuit_rejections_total");
    private static readonly Counter<long> Transitions = Meter.CreateCounter<long>("ai_circuit_transitions_total");

    private readonly object gate = new();
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AICircuitBreaker> logger;
    private CircuitState state = CircuitState.Closed;
    private int consecutiveFailures;
    private DateTimeOffset openedAt;
    private bool trialInFlight;

    public AICircuitBreaker(IConfiguration configuration, TimeProvider timeProvider, ILogger<AICircuitBreaker> logger)
    {
        this.timeProvider = timeProvider;
        this.logger = logger;
        FailureThreshold = configuration.GetValue<int?>("AI:CircuitBreaker:FailureThreshold") ?? 5;
        BreakDuration = TimeSpan.FromSeconds(configuration.GetValue<int?>("AI:CircuitBreaker:BreakSeconds") ?? 30);
        if (FailureThreshold is < 1 or > 100 || BreakDuration < TimeSpan.FromSeconds(1) || BreakDuration > TimeSpan.FromMinutes(30))
            throw new InvalidOperationException(
                "AI:CircuitBreaker:FailureThreshold must be 1-100 and AI:CircuitBreaker:BreakSeconds 1-1800.");
    }

    public int FailureThreshold { get; }
    public TimeSpan BreakDuration { get; }

    public CircuitState State
    {
        get
        {
            lock (gate)
            {
                return state == CircuitState.Open && timeProvider.GetUtcNow() - openedAt >= BreakDuration
                    ? CircuitState.HalfOpen
                    : state;
            }
        }
    }

    /// <summary>False when the call must be rejected without contacting the provider.</summary>
    public bool TryAcquire()
    {
        lock (gate)
        {
            if (state == CircuitState.Closed)
                return true;
            if (state == CircuitState.Open && timeProvider.GetUtcNow() - openedAt >= BreakDuration)
                Transition(CircuitState.HalfOpen);
            if (state == CircuitState.HalfOpen && !trialInFlight)
            {
                trialInFlight = true;
                return true;
            }
            Rejections.Add(1);
            return false;
        }
    }

    public void RecordSuccess()
    {
        lock (gate)
        {
            consecutiveFailures = 0;
            trialInFlight = false;
            if (state != CircuitState.Closed)
                Transition(CircuitState.Closed);
        }
    }

    public void RecordFailure()
    {
        lock (gate)
        {
            trialInFlight = false;
            consecutiveFailures++;
            if (state == CircuitState.HalfOpen || consecutiveFailures >= FailureThreshold)
            {
                openedAt = timeProvider.GetUtcNow();
                if (state != CircuitState.Open)
                    Transition(CircuitState.Open);
            }
        }
    }

    /// <summary>A call that did not tell us anything about the provider (bad request, caller cancelled).</summary>
    public void RecordIgnored()
    {
        lock (gate)
        {
            trialInFlight = false;
        }
    }

    public static bool IsOutage(AIProviderException exception) =>
        exception.StatusCode is null or 429 or >= 500;

    private void Transition(CircuitState next)
    {
        logger.Log(
            next == CircuitState.Open ? LogLevel.Warning : LogLevel.Information,
            "AI provider circuit {From} -> {To} after {Failures} consecutive failures.",
            state,
            next,
            consecutiveFailures);
        state = next;
        Transitions.Add(1, new KeyValuePair<string, object?>("state", next.ToString()));
    }
}

/// <summary>Wraps the provider client with <see cref="AICircuitBreaker"/>.</summary>
public sealed class CircuitBreakingOpenAIClient(
    OpenAIClient inner,
    AICircuitBreaker breaker,
    Microsoft.Extensions.Options.IOptions<AIOptions> options) : IOpenAIClient
{
    public Task<AIChatCompletion> CompleteAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        CancellationToken cancellationToken) =>
        ExecuteAsync(token => inner.CompleteAsync(messages, token), cancellationToken);

    public Task<AIChatCompletion> CompleteJsonAsync(
        IReadOnlyList<AIChatPromptMessage> messages,
        CancellationToken cancellationToken) =>
        ExecuteAsync(token => inner.CompleteJsonAsync(messages, token), cancellationToken);

    private async Task<AIChatCompletion> ExecuteAsync(
        Func<CancellationToken, Task<AIChatCompletion>> call,
        CancellationToken cancellationToken)
    {
        // AI disabled or without a key is a configuration state, not a provider outage.
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return await call(cancellationToken);
        if (!breaker.TryAcquire())
            throw new AIProviderException("AI provider circuit is open.", 503);
        try
        {
            var result = await call(cancellationToken);
            breaker.RecordSuccess();
            return result;
        }
        catch (AIProviderException exception) when (AICircuitBreaker.IsOutage(exception))
        {
            breaker.RecordFailure();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            breaker.RecordIgnored();
            throw;
        }
        catch (OperationCanceledException)
        {
            breaker.RecordFailure();
            throw;
        }
        catch
        {
            breaker.RecordIgnored();
            throw;
        }
    }
}

