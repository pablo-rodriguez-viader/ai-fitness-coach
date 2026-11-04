using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Profiles;

public sealed class GetUserProfileUseCase(IUserProfileRepository repository)
{
    public async Task<UserProfileResult?> ExecuteAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var profile = await repository.GetByUserIdAsync(userId, cancellationToken);
        return profile is null ? null : ToResult(profile);
    }

    internal static UserProfileResult ToResult(UserProfile profile) =>
        new(
            profile.UserId,
            profile.DisplayName,
            profile.Age,
            profile.HeightCentimeters,
            profile.Goal,
            profile.CreatedAt,
            profile.UpdatedAt);
}
