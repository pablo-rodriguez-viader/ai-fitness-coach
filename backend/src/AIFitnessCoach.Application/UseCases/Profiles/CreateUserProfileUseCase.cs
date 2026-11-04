using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Application.Errors;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Profiles;

public sealed class CreateUserProfileUseCase(
    IUserProfileRepository repository,
    TimeProvider timeProvider)
{
    public async Task<UserProfileResult> ExecuteAsync(
        string userId,
        CreateUserProfileCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw UseCaseValidationException.For("userId", "A valid authenticated user is required.");
        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Trim().Length > 120)
            throw UseCaseValidationException.For("displayName", "Display name must contain 1 to 120 characters.");
        if (command.Age is < 13 or > 120)
            throw UseCaseValidationException.For("age", "Age must be between 13 and 120.");
        if (!double.IsFinite(command.HeightCentimeters)
            || command.HeightCentimeters is <= 0 or > 300)
            throw UseCaseValidationException.For("heightCentimeters", "Height must be greater than 0 and at most 300 centimeters.");
        if (string.IsNullOrWhiteSpace(command.Goal) || command.Goal.Trim().Length > 64)
            throw UseCaseValidationException.For("goal", "Goal must contain 1 to 64 characters.");

        UserProfile profile;
        var existing = await repository.GetByUserIdAsync(userId, cancellationToken);
        try
        {
            if (existing is null)
            {
                profile = UserProfile.Create(
                    userId,
                    command.DisplayName,
                    command.Age,
                    command.HeightCentimeters,
                    command.Goal,
                    timeProvider.GetUtcNow());
            }
            else
            {
                existing.Update(
                    command.DisplayName,
                    command.Age,
                    command.HeightCentimeters,
                    command.Goal,
                    timeProvider.GetUtcNow());
                profile = existing;
            }
        }
        catch (ArgumentException exception)
        {
            throw UseCaseValidationException.For("profile", exception.Message);
        }

        await repository.SaveAsync(profile, cancellationToken);
        return GetUserProfileUseCase.ToResult(profile);
    }
}
