using Microsoft.AspNetCore.Mvc;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Api.Middleware;

/// <summary>Central place mapping domain exceptions to RFC 7807 Problem Details,
/// so every endpoint returns a consistent, structured error shape.</summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var (status, title, type) = Map(ex);

            if (status >= 500)
                _logger.LogError(ex, "Unhandled exception -> {Status} {Title}", status, title);
            else
                _logger.LogWarning("{Title}: {Message}", title, ex.Message);

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = type,
                Detail = ex.Message,
                Instance = context.Request.Path
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    private static (int Status, string Title, string Type) Map(Exception ex) => ex switch
    {
        WalletNotFoundException => (StatusCodes.Status404NotFound, "Wallet not found",
            "https://novawallet.firstbank/errors/wallet-not-found"),
        InsufficientFundsException => (StatusCodes.Status422UnprocessableEntity, "Insufficient funds",
            "https://novawallet.firstbank/errors/insufficient-funds"),
        DailyLimitExceededException => (StatusCodes.Status422UnprocessableEntity, "Daily transfer limit exceeded",
            "https://novawallet.firstbank/errors/daily-limit-exceeded"),
        IdempotencyKeyConflictException => (StatusCodes.Status409Conflict, "Idempotency-Key conflict",
            "https://novawallet.firstbank/errors/idempotency-conflict"),
        IdempotencyRequestInProgressException => (StatusCodes.Status409Conflict, "Request already in progress",
            "https://novawallet.firstbank/errors/idempotency-in-progress"),
        ForbiddenOperationException => (StatusCodes.Status403Forbidden, "Forbidden",
            "https://novawallet.firstbank/errors/forbidden"),
        InvalidTransferException => (StatusCodes.Status400BadRequest, "Invalid request",
            "https://novawallet.firstbank/errors/invalid-request"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred",
            "https://novawallet.firstbank/errors/internal")
    };
}
