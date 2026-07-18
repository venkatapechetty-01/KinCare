using KinCare.API.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace KinCare.API.Infrastructure;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.TraceIdentifier;
        var userId = httpContext.User.FindFirst("sub")?.Value ?? "anonymous";
        var path = httpContext.Request.Path;
        var method = httpContext.Request.Method;

        var (statusCode, title, logLevel) = exception switch
        {
            PlanGateException      => (StatusCodes.Status402PaymentRequired, "Plan upgrade required",        LogLevel.Warning),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden",                    LogLevel.Warning),
            KeyNotFoundException   => (StatusCodes.Status404NotFound,       exception.Message,               LogLevel.Warning),
            InvalidOperationException => (StatusCodes.Status400BadRequest,  exception.Message,               LogLevel.Warning),
            ArgumentException      => (StatusCodes.Status400BadRequest,     exception.Message,               LogLevel.Warning),
            System.Text.Json.JsonException => (StatusCodes.Status400BadRequest, "Invalid JSON in request body", LogLevel.Warning),
            // Minimal API's [FromBody] record binding wraps malformed-body failures (including
            // a string that doesn't match an enum member, e.g. a stale role name) in this type
            // rather than a bare JsonException — without this case every one of those fell
            // through to the 500 default instead of a clean 400.
            Microsoft.AspNetCore.Http.BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request body", LogLevel.Warning),
            _                      => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", LogLevel.Error),
        };

        _logger.Log(
            logLevel,
            exception,
            "Unhandled {ExceptionType}: {Method} {Path} → {StatusCode} | User={UserId} CorrelationId={CorrelationId}",
            exception.GetType().Name, method, path, statusCode, userId, correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = path,
        };

        // Surface plan tier requirement for 402 so the client can show the right upgrade prompt
        if (exception is PlanGateException pge)
            problemDetails.Extensions["requiredTier"] = pge.RequiredTier.ToString();

        // Include correlation ID so support can cross-reference logs
        problemDetails.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
