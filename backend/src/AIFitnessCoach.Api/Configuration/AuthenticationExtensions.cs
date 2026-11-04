using System.Security.Claims;
using System.Text;
using AIFitnessCoach.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AIFitnessCoach.Api.Configuration;

public static class AuthenticationExtensions
{
    public static void AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = Required(configuration, "Jwt:Issuer");
        var audience = Required(configuration, "Jwt:Audience");
        _ = Required(configuration, "Apple:Audience");
        var encodedKey = Required(configuration, "Jwt:SigningKeyBase64");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Jwt:SigningKeyBase64 must be valid Base64.", exception);
        }

        if (key.Length < 32)
            throw new InvalidOperationException("JWT signing key must contain at least 256 bits.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var sessionValue = context.Principal?.FindFirstValue("sid");
                        if (!Guid.TryParse(sessionValue, out var sessionId))
                        {
                            context.Fail("A valid session claim is required.");
                            return;
                        }

                        var repository = context.HttpContext.RequestServices
                            .GetRequiredService<IAuthRepository>();
                        var active = await repository.IsSessionActiveAsync(
                            sessionId,
                            DateTimeOffset.UtcNow,
                            context.HttpContext.RequestAborted);
                        if (!active)
                            context.Fail("The authentication session has expired or been revoked.");
                    }
                };
            });
        services.AddAuthorization();
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required.");
}

public interface ICurrentUser
{
    string GetRequiredUserId(ClaimsPrincipal principal);
}

public sealed class CurrentUser : ICurrentUser
{
    public string GetRequiredUserId(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("sub");
        return !string.IsNullOrWhiteSpace(userId)
            ? userId
            : throw new UnauthorizedAccessException("A validated subject claim is required.");
    }
}
