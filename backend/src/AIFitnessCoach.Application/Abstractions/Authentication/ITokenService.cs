namespace AIFitnessCoach.Application.Abstractions.Authentication;

public interface ITokenService
{
    string CreateAccessToken(string userId, Guid sessionId, DateTimeOffset now);
    string CreateRefreshToken();
    string HashRefreshToken(string token);
}
