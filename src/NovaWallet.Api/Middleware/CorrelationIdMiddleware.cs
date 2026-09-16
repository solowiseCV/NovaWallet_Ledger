namespace NovaWallet.Api.Middleware;

/// <summary>Propagates/creates an X-Correlation-Id and attaches it to the logging
/// scope so every log line for a request (across layers) can be correlated —
/// this is the "structured logging with correlation IDs" stretch goal.</summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : context.TraceIdentifier;

        context.Response.Headers["X-Correlation-Id"] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
