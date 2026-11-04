using System.Text.Json;
using System.Diagnostics.Metrics;
using AIFitnessCoach.AI.Models;
using AIFitnessCoach.Application.UseCases.AI;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.AI;

public sealed class CoachConsentStore(FitnessDbContext dbContext) : ICoachConsentStore
{
    public async Task<CoachConsent> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var consent = await dbContext.AIConsents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        // A consent to an older text (before the provider was named) must be given again.
        return consent is null
            ? new CoachConsent(false, 0, null)
            : new CoachConsent(
                consent.Granted && consent.Version >= CoachConsentVersion.Current,
                consent.Version,
                consent.GrantedAt);
    }

    public async Task SetAsync(
        string userId,
        bool granted,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_consents (user_id, granted, version, granted_at, updated_at)
            VALUES ({userId}, {granted}, {version}, {(granted ? now : null)}, {now})
            ON CONFLICT (user_id) DO UPDATE SET
                granted = EXCLUDED.granted,
                version = EXCLUDED.version,
                granted_at = EXCLUDED.granted_at,
                updated_at = EXCLUDED.updated_at
            """, cancellationToken);
        if (rows != 1)
            throw new InvalidOperationException("AI consent could not be saved.");
    }
}

public sealed class CoachIdempotencyStore(
    FitnessDbContext dbContext,
    TimeProvider timeProvider) : ICoachIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CoachMessageResponse?> GetCompletedAsync(
        string userId,
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.AICoachRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.ClientRequestId == clientRequestId,
                cancellationToken);
        if (request is null || request.Status != "Completed" || request.ResponseJson is null)
            return null;
        return JsonSerializer.Deserialize<CoachMessageResponse>(request.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored AI idempotency response is invalid.");
    }

    public async Task<bool> TryBeginAsync(
        string userId,
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM ai_coach_requests
            WHERE created_at < {now.AddDays(-1)}
            """, cancellationToken);
        var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_coach_requests (user_id, client_request_id, status, response_json, created_at, completed_at)
            VALUES ({userId}, {clientRequestId}, 'Processing', NULL, {now}, NULL)
            ON CONFLICT (user_id, client_request_id) DO NOTHING
            """, cancellationToken);
        return rows == 1;
    }

    public async Task CompleteAsync(
        string userId,
        Guid clientRequestId,
        CoachMessageResponse response,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.AICoachRequests.SingleOrDefaultAsync(
            item => item.UserId == userId
                && item.ClientRequestId == clientRequestId
                && item.Status == "Processing",
            cancellationToken)
            ?? throw new InvalidOperationException("AI idempotency reservation is unavailable.");
        row.Complete(response.ConversationId, JsonSerializer.Serialize(response, JsonOptions), timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AbandonAsync(
        string userId,
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        await dbContext.AICoachRequests
            .Where(item => item.UserId == userId
                && item.ClientRequestId == clientRequestId
                && item.Status == "Processing")
            .ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed class CoachFeedbackStore(
    FitnessDbContext dbContext) : ICoachFeedbackStore
{
    private static readonly Meter Meter = new("AIFitnessCoach.Coach");
    private static readonly Counter<long> Feedback = Meter.CreateCounter<long>("coach_feedback_total");

    public async Task<bool> AddAsync(
        string userId,
        Guid conversationId,
        Guid messageId,
        string rating,
        string? reasonCode,
        string? comment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("Feedback comments are not accepted to avoid storing sensitive data.");
        var messageIsOwned = await dbContext.AIMessages.AsNoTracking()
            .AnyAsync(message => message.Id == messageId
                && message.ConversationId == conversationId
                && message.Role == MessageRole.Assistant
                && dbContext.AIConversations.Any(conversation =>
                    conversation.Id == conversationId && conversation.UserId == userId),
                cancellationToken);
        if (!messageIsOwned)
            return false;
        var feedback = AIFeedback.Create(
            userId, conversationId, messageId, rating, reasonCode, now);
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_feedback (id, user_id, conversation_id, message_id, rating, reason_code, created_at)
            VALUES ({feedback.Id}, {feedback.UserId}, {feedback.ConversationId}, {feedback.MessageId},
                {feedback.Rating}, {feedback.ReasonCode}, {feedback.CreatedAt})
            ON CONFLICT (user_id, message_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 1)
            Feedback.Add(1, new KeyValuePair<string, object?>("rating", rating));
        return inserted == 1;
    }
}

public sealed class CoachMemoryStore(
    FitnessDbContext dbContext) : ICoachMemoryStore
{
    public async Task<CoachConversationMemory?> GetSummaryAsync(
        string userId,
        Guid conversationId,
        int contextVersion,
        CancellationToken cancellationToken)
    {
        var summary = await dbContext.AIConversationSummaries.AsNoTracking()
            .Where(item => item.ConversationId == conversationId
                && item.ContextVersion == contextVersion
                && dbContext.AIConversations.Any(conversation =>
                    conversation.Id == conversationId && conversation.UserId == userId))
            .Select(item => new CoachConversationMemory(item.Summary, item.MessageCountIncluded))
            .SingleOrDefaultAsync(cancellationToken);
        return summary;
    }

    public async Task SaveSummaryAsync(
        string userId,
        Guid conversationId,
        string summary,
        int contextVersion,
        string promptVersion,
        int messageCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var isOwned = await dbContext.AIConversations.AnyAsync(
            item => item.Id == conversationId && item.UserId == userId,
            cancellationToken);
        if (!isOwned)
            throw new ActivityAnalysisConversationNotFoundException();
        var record = await dbContext.AIConversationSummaries
            .SingleOrDefaultAsync(item => item.ConversationId == conversationId, cancellationToken);
        if (record is null)
        {
            dbContext.AIConversationSummaries.Add(AIConversationSummaryRecord.Create(
                conversationId, summary, contextVersion, promptVersion, messageCount, now));
        }
        else
        {
            record.Update(summary, contextVersion, promptVersion, messageCount, now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
