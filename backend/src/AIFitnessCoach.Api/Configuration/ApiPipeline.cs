using AIFitnessCoach.Api.Endpoints;

namespace AIFitnessCoach.Api.Configuration;

public static class ApiPipeline
{
    /// <summary>Middleware order and endpoints shared by Program and the end-to-end tests.</summary>
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseOperationalMiddleware();
        app.UseMiddleware<ApiRequestMetricsMiddleware>();
        app.UseMiddleware<ApiExceptionHandlerMiddleware>();
        // Inside the exception handler: a timed-out request becomes 504, not 500.
        app.UseRequestTimeouts();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        // After authentication so limits partition by user, but before authorization so
        // requests with invalid or missing tokens are limited too.
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapOperationalEndpoints();
        app.MapApiEndpoints();
        return app;
    }
}
