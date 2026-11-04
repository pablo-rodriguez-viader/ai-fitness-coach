using AIFitnessCoach.Application.Errors;
using Microsoft.AspNetCore.Mvc;

namespace AIFitnessCoach.Api.Configuration;

public sealed class ApiExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (context.Response.HasStarted)
        {
            // Headers are already sent; the only safe option is to log and abort.
            logger.LogError(
                exception,
                "Request failed after the response started. TraceId: {TraceId}",
                context.TraceIdentifier);
            throw;
        }
        catch (UseCaseValidationException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new HttpValidationProblemDetails(
                    exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Extensions = { ["traceId"] = context.TraceIdentifier }
                },
                cancellationToken: context.RequestAborted);
        }
        catch (BadHttpRequestException exception)
        {
            // Malformed JSON or bodies that cannot be bound: a client error, not a 500.
            logger.LogInformation(
                "Rejected malformed request. TraceId: {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = exception.StatusCode is >= 400 and < 500
                ? exception.StatusCode
                : StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = context.Response.StatusCode,
                    Title = "The request body is invalid.",
                    Extensions = { ["traceId"] = context.TraceIdentifier }
                },
                cancellationToken: context.RequestAborted);
        }
        catch (AIFitnessCoach.Application.UseCases.Authentication.BetaFullException exception)
        {
            logger.LogWarning("Sign-up rejected: private beta is full ({MaxUsers} users).", exception.MaxUsers);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "The private beta is full.",
                    Extensions = { ["code"] = "beta_full", ["traceId"] = context.TraceIdentifier }
                },
                cancellationToken: context.RequestAborted);
        }
        catch (UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled request failure. TraceId: {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Extensions = { ["traceId"] = context.TraceIdentifier }
                },
                cancellationToken: context.RequestAborted);
        }
    }
}
