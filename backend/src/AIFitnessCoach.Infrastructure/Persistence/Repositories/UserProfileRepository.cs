using AIFitnessCoach.Application.Abstractions.Persistence;
using AIFitnessCoach.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIFitnessCoach.Infrastructure.Persistence.Repositories;

public sealed class UserProfileRepository(FitnessDbContext dbContext) : IUserProfileRepository
{
    public Task<UserProfile?> GetByUserIdAsync(
        string userId,
        CancellationToken cancellationToken) =>
        dbContext.UserProfiles
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task SaveAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(profile).State == EntityState.Detached)
        {
            dbContext.UserProfiles.Add(profile);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
