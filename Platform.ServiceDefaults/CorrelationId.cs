using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Platform.ServiceDefaults;

// CorrelationId is created once at the entrance of the system and travels through all services.
// It differs from TraceId: the application creates it (business id of the request), TraceId is created by OpenTelemetry.
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var isNew = !context.Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrEmpty(value);
        if (isNew)
        {
            value = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = value;        // YARP and HttpClient will see it further
        }

        var correlationId = value.ToString();

        // OnStarting: UseExceptionHandler clears headers, the id must survive in an error response too
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            if (isNew)
                logger.LogInformation("New CorrelationId {CorrelationId} generated", correlationId);

            logger.LogInformation("Request started {Method} {Path}", context.Request.Method, context.Request.Path);
            await next(context);
            logger.LogInformation("Request finished with {StatusCode}", context.Response.StatusCode);
        }
    }
}

// Adds the id of the current request to every outgoing HTTP call
public class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(id))
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);

        return base.SendAsync(request, ct);
    }
}
