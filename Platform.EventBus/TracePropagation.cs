using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

// The trace context goes through the broker in message headers (W3C traceparent), so the Dashboard shows
// one trace: HTTP request -> publish -> processing in another service -> database.
// RabbitMQ.Client 7 has its own instrumentation (RabbitMQ.Client.OpenTelemetry); here it is done by hand
// to show how the context crosses the broker. The source is added in ServiceDefaults (AddSource).
public static class TracePropagation
{
    public const string SourceName = "Platform.Messaging";
    public static readonly ActivitySource Source = new(SourceName);

    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    // Producer span; the parent is the HTTP request or the context saved in the outbox
    public static Activity? StartPublishActivity(string exchange, string routingKey, string eventType, string messageId,
        ActivityContext parent = default)
    {
        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "send");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", messageId);
        activity?.SetTag("messaging.event.type", eventType);
        return activity;
    }

    public static void Inject(Activity? activity, IDictionary<string, object?> headers)
    {
        if (activity is null)
            return;
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), headers,
            (h, key, value) => h[key] = value);
    }

    // traceparent as text, to be stored in the outbox row and continued by the background publisher
    public static string? CurrentTraceParent()
    {
        if (Activity.Current is not { } activity)
            return null;
        var carrier = new Dictionary<string, string>();
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), carrier, (c, k, v) => c[k] = v);
        return carrier.GetValueOrDefault("traceparent");
    }

    public static ActivityContext ParseTraceParent(string? traceParent)
        => traceParent is not null && ActivityContext.TryParse(traceParent, null, out var context) ? context : default;

    // Consumer span as a child of the context from the headers; values of headers arrive as byte[]
    public static Activity? StartConsumerActivity(BasicDeliverEventArgs ea, string queue)
    {
        var parent = Propagator.Extract(default, ea.BasicProperties.Headers,
            (headers, key) => headers is not null && headers.TryGetValue(key, out var v) && v is byte[] bytes
                ? [Encoding.UTF8.GetString(bytes)]
                : []);

        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", ea.RoutingKey);
        activity?.SetTag("messaging.message.id", ea.BasicProperties.MessageId);
        activity?.SetTag("messaging.event.type", ea.BasicProperties.Type);
        return activity;
    }
}
