using Serilog.Context;

namespace Catalog.Api.Middleware;

// Takes X-Correlation-Id from the request (or creates one) and pushes it into every log event
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", context.User.Identity?.Name ?? "anonymous"))
        {
            await next(context);
        }
    }
}
