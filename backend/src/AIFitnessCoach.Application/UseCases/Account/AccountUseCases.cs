using System.Diagnostics.Metrics;
using AIFitnessCoach.Application.Abstractions.Authentication;
using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Account;

public sealed record AccountExport(
    string Format,
    DateTimeOffset GeneratedAt,
    string UserId,
    IReadOnlyList<AccountExportTable> Tables);

public sealed record AccountDeletionResult(bool Deleted, string AppleRevocation);

/// <summary>Data portability: a JSON copy of everything the service stores about the user.</summary>
public sealed class ExportAccountDataUseCase(IAccountDataStore store, TimeProvider timeProvider)
{
    public const string Format = "aifitnesscoach-export-v1";
    private static readonly Meter Meter = new("AIFitnessCoach.Account");
    private static readonly Counter<long> Exports = Meter.CreateCounter<long>("account_exports_total");

    public async Task<AccountExport?> ExecuteAsync(string userId, CancellationToken cancellationToken)
    {
        var tables = await store.ExportAsync(userId, cancellationToken);
        if (tables is null)
            return null;
        Exports.Add(1);
        return new AccountExport(Format, timeProvider.GetUtcNow(), userId, tables);
    }
}

/// <summary>
/// Deletes the account and all its data (health samples, AI conversations, workouts, nutrition,
/// goals, analytics events, support reports, sessions). The Apple grant is revoked first; a failed
/// revocation is logged and counted but never keeps the user's data.
/// </summary>
public sealed class DeleteAccountUseCase(IAccountDataStore store, IAppleTokenRevoker appleTokenRevoker)
{
    private static readonly Meter Meter = new("AIFitnessCoach.Account");
    private static readonly Counter<long> Deletions = Meter.CreateCounter<long>("account_deletions_total");
    private static readonly Counter<long> RevocationProblems =
        Meter.CreateCounter<long>("account_apple_revocation_problems_total");

    public async Task<AccountDeletionResult> ExecuteAsync(
        string userId,
        string? appleAuthorizationCode,
        CancellationToken cancellationToken)
    {
        if (appleAuthorizationCode is { Length: > 4096 })
            throw UseCaseValidationException.For("authorizationCode", "The authorization code is too long.");

        var revocation = await appleTokenRevoker.RevokeAsync(appleAuthorizationCode, cancellationToken);
        if (revocation != AppleRevocationOutcome.Revoked)
            RevocationProblems.Add(1, new KeyValuePair<string, object?>("outcome", revocation.ToString()));

        var deleted = await store.DeleteAsync(userId, cancellationToken);
        if (deleted)
            Deletions.Add(1);
        return new AccountDeletionResult(deleted, revocation.ToString());
    }
}

public sealed record SubmitSupportReportCommand(
    string Kind,
    string? Category,
    string? Message,
    int? Rating,
    string? ContactEmail,
    string? AppVersion,
    string? BuildNumber,
    string? OsVersion,
    string? DeviceModel,
    IReadOnlyDictionary<string, string>? Diagnostics);

/// <summary>Feedback Center, bug reports and support requests sent from the app.</summary>
public sealed class SubmitSupportReportUseCase(ISupportReportRepository repository, TimeProvider timeProvider)
{
    /// <summary>Generous for a tester, but stops a stuck client from filling the table.</summary>
    public const int MaximumReportsPerDay = 20;
    public const int MaximumDiagnosticEntries = 40;

    private static readonly Meter Meter = new("AIFitnessCoach.Support");
    private static readonly Counter<long> Reports = Meter.CreateCounter<long>("support_reports_total");

    public async Task<SupportReport> ExecuteAsync(
        string userId,
        SubmitSupportReportCommand command,
        CancellationToken cancellationToken)
    {
        var kind = command.Kind.Trim().ToLowerInvariant();
        if (!SupportReportKinds.All.Contains(kind))
            throw UseCaseValidationException.For("kind", "Kind must be feedback, bug or support.");
        var message = command.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            throw UseCaseValidationException.For("message", "A message is required.");
        if (message.Length > SupportReport.MaximumMessageLength)
            throw UseCaseValidationException.For(
                "message",
                $"The message cannot exceed {SupportReport.MaximumMessageLength} characters.");
        if (command.Rating is < 1 or > 5)
            throw UseCaseValidationException.For("rating", "Rating must be between 1 and 5.");
        var email = string.IsNullOrWhiteSpace(command.ContactEmail) ? null : command.ContactEmail.Trim();
        if (email is not null && (email.Length > SupportReport.MaximumEmailLength
            || email.IndexOf('@') is <= 0
            || email.IndexOf('@') == email.Length - 1))
            throw UseCaseValidationException.For("contactEmail", "The contact email is invalid.");
        if (command.Diagnostics is { Count: > MaximumDiagnosticEntries })
            throw UseCaseValidationException.For("diagnostics", "Too many diagnostic entries.");
        var diagnostics = command.Diagnostics?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 40)
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value is { Length: > 300 } value ? value[..300] : pair.Value ?? string.Empty,
                StringComparer.Ordinal);

        var now = timeProvider.GetUtcNow();
        if (await repository.CountSinceAsync(userId, now.AddDays(-1), cancellationToken) >= MaximumReportsPerDay)
            throw UseCaseValidationException.For("message", "Daily report limit reached. Please try again tomorrow.");

        SupportReport report;
        try
        {
            report = SupportReport.Create(
                userId, kind, command.Category, message, command.Rating, email,
                command.AppVersion, command.BuildNumber, command.OsVersion, command.DeviceModel,
                diagnostics, now);
        }
        catch (ArgumentException exception)
        {
            throw UseCaseValidationException.For(exception.ParamName ?? "report", exception.Message);
        }
        await repository.AddAsync(report, cancellationToken);
        Reports.Add(1, new KeyValuePair<string, object?>("kind", kind));
        return report;
    }
}
