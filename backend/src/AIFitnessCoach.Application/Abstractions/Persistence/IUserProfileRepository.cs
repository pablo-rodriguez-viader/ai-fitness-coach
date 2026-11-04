using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.Abstractions.Persistence;

public interface IUserProfileRepository
{
    Task<UserProfile?> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
    Task SaveAsync(UserProfile profile, CancellationToken cancellationToken);
}
