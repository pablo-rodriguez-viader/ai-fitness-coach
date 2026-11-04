namespace AIFitnessCoach.Api.Contracts.Requests;

public sealed record CoachMessageRequest(
    string? Message,
    Guid ClientRequestId,
    string Period = "auto");

public sealed record CoachConsentRequest(bool Granted);

public sealed record CoachFeedbackRequest(
    Guid MessageId,
    string? Rating,
    string? ReasonCode,
    string? Comment);
