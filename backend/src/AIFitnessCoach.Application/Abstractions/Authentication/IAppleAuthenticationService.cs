using AIFitnessCoach.Application.Abstractions.Persistence;

namespace AIFitnessCoach.Application.Abstractions.Authentication;

public interface IAppleAuthenticationService
{
    Task<AppleIdentity> ValidateIdentityTokenAsync(
        string identityToken,
        string rawNonce,
        CancellationToken cancellationToken);
}
