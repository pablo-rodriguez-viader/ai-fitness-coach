using AIFitnessCoach.Application.UseCases.AI;
using AIFitnessCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.AI;

public sealed class ActivityAnalysisQuota(FitnessDbContext dbContext) : IActivityAnalysisQuota
{
    public async Task<bool> TryReserveAsync(
        string userId,
        DateOnly date,
        int dailyLimit,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_analysis_daily_usage (user_id, usage_date, request_count)
            VALUES ({userId}, {date}, 1)
            ON CONFLICT (user_id, usage_date) DO UPDATE
            SET request_count = ai_analysis_daily_usage.request_count + 1
            WHERE ai_analysis_daily_usage.request_count < {dailyLimit}
            """, cancellationToken);
        return rows == 1;
    }

    public async Task<bool> TryReserveTokensAsync(
        string userId,
        DateOnly date,
        int tokenReserve,
        int dailyTokenLimit,
        CancellationToken cancellationToken)
    {
        if (tokenReserve < 1 || dailyTokenLimit < 1 || tokenReserve > dailyTokenLimit)
            return false;
        var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_analysis_daily_usage (user_id, usage_date, request_count, token_count)
            VALUES ({userId}, {date}, 1, {tokenReserve})
            ON CONFLICT (user_id, usage_date) DO UPDATE
            SET token_count = ai_analysis_daily_usage.token_count + {tokenReserve}
            WHERE ai_analysis_daily_usage.token_count + {tokenReserve} <= {dailyTokenLimit}
            """, cancellationToken);
        return rows == 1;
    }

    public async Task SettleTokensAsync(
        string userId,
        DateOnly date,
        int reservedTokens,
        int actualTokens,
        CancellationToken cancellationToken)
    {
        if (reservedTokens < 0 || actualTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(reservedTokens));
        var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE ai_analysis_daily_usage
            SET token_count = GREATEST(token_count - {reservedTokens} + {actualTokens}, 0)
            WHERE user_id = {userId} AND usage_date = {date}
            """, cancellationToken);
        if (rows != 1)
            throw new InvalidOperationException("Daily AI token reservation could not be settled.");
    }
}
