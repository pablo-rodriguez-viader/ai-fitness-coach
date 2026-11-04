namespace AIFitnessCoach.Application.Errors;

public sealed class UseCaseValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("Request validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static UseCaseValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
