using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Platform.EventBus;

// CorrelationId of the current HTTP request: it goes into the event so the logs of the consumers can be found by it
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}

public sealed class HttpCorrelationIdAccessor(IHttpContextAccessor accessor) : ICorrelationIdAccessor
{
    public string? CorrelationId
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers["X-Correlation-Id"].ToString();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}

public static class EventBusExtensions
{
    public const string ConnectionName = "messaging";      // the name of the resource in AppHost

    public static IHostApplicationBuilder AddEventBus(this IHostApplicationBuilder builder)
    {
        // Aspire.RabbitMQ.Client: one IConnection for the application (singleton) and a health check.
        // Its automatic tracing is off: the trace context is propagated by hand in TracePropagation.
        builder.AddRabbitMQClient(ConnectionName, settings => settings.DisableTracing = true);

        builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICorrelationIdAccessor, HttpCorrelationIdAccessor>();
        return builder;
    }
}
