using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

/// <summary>
/// Finds the user's rows from the EF model instead of a hand-written list, so a table added later
/// is exported and deleted without touching this class. A table belongs to the user when it has a
/// <c>UserId</c> property, or a foreign key to a table that belongs to the user.
/// </summary>
public sealed class AccountDataStore(FitnessDbContext dbContext) : IAccountDataStore
{
    /// <summary>Hashes of session secrets: useless to the user and unsafe to hand out.</summary>
    private static readonly HashSet<string> TablesExcludedFromExport = new(StringComparer.Ordinal) { "refresh_tokens" };

    private static readonly MethodInfo SetMethod = typeof(DbContext)
        .GetMethods()
        .Single(method => method.Name == nameof(DbContext.Set)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 0);

    private static readonly MethodInfo ToListMethod = typeof(AccountDataStore)
        .GetMethod(nameof(ToObjectListAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task<IReadOnlyList<AccountExportTable>?> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var rowsByType = await LoadUserRowsAsync(userId, cancellationToken);
        if (rowsByType is null)
            return null;
        try
        {
            return rowsByType
                .Select(pair => (Type: pair.Key, Rows: pair.Value, Table: pair.Key.GetTableName() ?? pair.Key.ClrType.Name))
                .Where(item => !TablesExcludedFromExport.Contains(item.Table))
                .OrderBy(item => item.Table, StringComparer.Ordinal)
                .Select(item => new AccountExportTable(
                    item.Table,
                    item.Rows.Select(row => ToColumns(item.Type, row)).ToList()))
                .ToList();
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            // Every user table has ON DELETE CASCADE to users (directly or through its parent),
            // so one statement removes everything atomically without loading health samples.
            var strategy = dbContext.Database.CreateExecutionStrategy();
            var deleted = await strategy.ExecuteAsync(async () =>
                await dbContext.Users.Where(user => user.Id == userId).ExecuteDeleteAsync(cancellationToken));
            return deleted > 0;
        }

        // Providers without database cascades (tests): remove each row explicitly.
        var rowsByType = await LoadUserRowsAsync(userId, cancellationToken);
        if (rowsByType is null)
            return false;
        foreach (var rows in rowsByType.Values)
            dbContext.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return true;
    }

    public async Task<int> DeleteAIDataAsync(string userId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                // Messages, summaries, feedback, request records, token usage and snapshots of a
                // conversation go with it (ON DELETE CASCADE); snapshots without one are deleted too.
                var conversations = await dbContext.AIConversations
                    .Where(item => item.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.AIContextSnapshots.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await dbContext.AICoachRequests.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await dbContext.AIFeedback.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return conversations;
            });
        }

        var conversationRows = await dbContext.AIConversations.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var ids = conversationRows.Select(item => item.Id).ToList();
        dbContext.AIMessages.RemoveRange(await dbContext.AIMessages.Where(item => ids.Contains(item.ConversationId)).ToListAsync(cancellationToken));
        dbContext.AIConversationSummaries.RemoveRange(
            await dbContext.AIConversationSummaries.Where(item => ids.Contains(item.ConversationId)).ToListAsync(cancellationToken));
        dbContext.AIContextSnapshots.RemoveRange(await dbContext.AIContextSnapshots.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        dbContext.AICoachRequests.RemoveRange(await dbContext.AICoachRequests.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        dbContext.AIFeedback.RemoveRange(await dbContext.AIFeedback.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        dbContext.AIConversations.RemoveRange(conversationRows);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return conversationRows.Count;
    }

    public async Task<int> DeleteHealthDataAsync(string userId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
            return await dbContext.HealthMetrics.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var rows = await dbContext.HealthMetrics.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        dbContext.HealthMetrics.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return rows.Count;
    }

    /// <summary>The tables that belong to a user, parents before children.</summary>
    public static IReadOnlyList<IEntityType> UserOwnedEntityTypes(IModel model)
    {
        var userType = model.FindEntityType(typeof(User))!;
        var ordered = new List<IEntityType> { userType };
        var owned = new HashSet<IEntityType> { userType };
        var remaining = model.GetEntityTypes().Where(type => !type.IsOwned() && type != userType).ToList();
        bool added;
        do
        {
            added = false;
            foreach (var type in remaining.ToList())
            {
                var ownsByUserId = type.FindProperty("UserId") is not null;
                var ownsByParent = type.GetForeignKeys().Any(key => owned.Contains(key.PrincipalEntityType));
                if (!ownsByUserId && !ownsByParent)
                    continue;
                ordered.Add(type);
                owned.Add(type);
                remaining.Remove(type);
                added = true;
            }
        } while (added);
        return ordered;
    }

    private async Task<Dictionary<IEntityType, List<object>>?> LoadUserRowsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
            return null;

        var rowsByType = new Dictionary<IEntityType, List<object>>();
        foreach (var type in UserOwnedEntityTypes(dbContext.Model))
        {
            var rows = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            if (type.ClrType == typeof(User))
            {
                foreach (var row in await QueryAsync(type, EqualsFilter(type, "Id", userId), cancellationToken))
                    rows[row] = row;
            }
            if (type.FindProperty("UserId") is not null)
            {
                foreach (var row in await QueryAsync(type, EqualsFilter(type, "UserId", userId), cancellationToken))
                    rows[row] = row;
            }
            // Rows reached through a parent (messages of a conversation, days of a plan...).
            foreach (var foreignKey in type.GetForeignKeys())
            {
                if (foreignKey.Properties.Count != 1
                    || foreignKey.PrincipalKey.Properties.Count != 1
                    || foreignKey.PrincipalEntityType.ClrType == typeof(User)
                    || !rowsByType.TryGetValue(foreignKey.PrincipalEntityType, out var parents)
                    || parents.Count == 0)
                    continue;
                var principalKey = foreignKey.PrincipalKey.Properties[0];
                var keyValues = parents
                    .Select(parent => dbContext.Entry(parent).Property(principalKey.Name).CurrentValue)
                    .Where(value => value is not null)
                    .Distinct()
                    .ToList();
                var dependent = foreignKey.Properties[0];
                foreach (var row in await QueryAsync(type, ContainsFilter(type, dependent, keyValues!), cancellationToken))
                    rows[row] = row;
            }
            rowsByType[type] = rows.Values.ToList();
        }
        return rowsByType;
    }

    private async Task<List<object>> QueryAsync(
        IEntityType type,
        LambdaExpression filter,
        CancellationToken cancellationToken)
    {
        var set = (IQueryable)SetMethod.MakeGenericMethod(type.ClrType).Invoke(dbContext, null)!;
        var where = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            [type.ClrType],
            set.Expression,
            Expression.Quote(filter));
        var query = set.Provider.CreateQuery(where);
        var task = (Task<List<object>>)ToListMethod.MakeGenericMethod(type.ClrType)
            .Invoke(null, [query, cancellationToken])!;
        return await task;
    }

    private static async Task<List<object>> ToObjectListAsync<T>(IQueryable query, CancellationToken cancellationToken)
    {
        var rows = await ((IQueryable<T>)query).ToListAsync(cancellationToken);
        return rows.Cast<object>().ToList();
    }

    private static LambdaExpression EqualsFilter(IEntityType type, string propertyName, string value)
    {
        var parameter = Expression.Parameter(type.ClrType, "row");
        var property = PropertyAccess(parameter, type.FindProperty(propertyName)!);
        return Expression.Lambda(Expression.Equal(property, Expression.Constant(value)), parameter);
    }

    private static LambdaExpression ContainsFilter(IEntityType type, IProperty property, IReadOnlyList<object> values)
    {
        var parameter = Expression.Parameter(type.ClrType, "row");
        var access = PropertyAccess(parameter, property);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.ClrType))!;
        foreach (var value in values)
            list.Add(value);
        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [property.ClrType],
            Expression.Constant(list),
            access);
        return Expression.Lambda(contains, parameter);
    }

    private static Expression PropertyAccess(ParameterExpression parameter, IProperty property) =>
        Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [property.ClrType],
            parameter,
            Expression.Constant(property.Name));

    private IReadOnlyDictionary<string, object?> ToColumns(IEntityType type, object row)
    {
        var entry = dbContext.Entry(row);
        var columns = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in type.GetProperties())
        {
            var value = entry.Property(property.Name).CurrentValue;
            columns[property.GetColumnName()] = value is Enum enumValue ? enumValue.ToString() : value;
        }
        return columns;
    }
}

public sealed class SupportReportRepository(FitnessDbContext dbContext) : ISupportReportRepository
{
    public async Task AddAsync(SupportReport report, CancellationToken cancellationToken)
    {
        dbContext.SupportReports.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupportReport>> ListAsync(
        string? kind,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SupportReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(report => report.Kind == kind);
        if (since is { } from)
            query = query.Where(report => report.CreatedAt >= from);
        return await query
            .OrderByDescending(report => report.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountSinceAsync(string userId, DateTimeOffset since, CancellationToken cancellationToken) =>
        dbContext.SupportReports.CountAsync(
            report => report.UserId == userId && report.CreatedAt >= since,
            cancellationToken);
}
