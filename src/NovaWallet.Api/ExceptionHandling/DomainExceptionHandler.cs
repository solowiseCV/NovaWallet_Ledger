using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Api.ExceptionHandling;

/// <summary>
/// Maps domain exceptions to RFC 7807 Problem Details using ASP.NET Core 8's
/// built-in IExceptionHandler + IProblemDetailsService, rather than a hand-rolled
/// try/catch middleware. This is the framework's own extension point for exactly
/// this job: it composes correctly with [ApiController]'s automatic
/// ValidationProblemDetails for model-binding errors (400s), and with
/// AddProblemDetails()'s CustomizeProblemDetails hook (Program.cs) for adding
/// the trace id to every problem response, success or failure.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<DomainExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, type) = Map(exception);

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception -> {Status} {Title}", status, title);
        else
            _logger.LogWarning("{Title}: {Message}", title, exception.Message);

        httpContext.Response.StatusCode = status;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = type,
                Detail = status >= 500
                    ? "An unexpected error occurred. Use the traceId when contacting support."
                    : exception.Message,
                Instance = httpContext.Request.Path
            }
        });
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
