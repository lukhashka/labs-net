using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.EventBus.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

// One handler class per event type; it is resolved in the DI scope of the message
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

// The data of the event can never be processed (business validation): a retry will not help, straight to the DLQ
public sealed class PermanentEventException(string message) : Exception(message);

// Long-running consumer of one queue with manual acknowledgement:
//   success                         -> BasicAck, the message leaves the queue
//   temporary failure (DB is down)  -> published again with x-retry-count + 1, the original is acked
//   retries exhausted or permanent  -> BasicNack(requeue: false) -> dead letter exchange -> DLQ
public abstract class RabbitMqConsumer : BackgroundService
{
    public const int MaxRetries = 3;
    private const string RetryHeader = "x-retry-count";

    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _logger;
    private readonly Dictionary<string, Registration> _handlers = [];
    private readonly Counter<long> _handled;
    private IChannel? _channel;
    private long _deadLetterDepth;

    protected RabbitMqConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger logger, IMeterFactory meters)
    {
        _connection = connection;
        _scopes = scopes;
        _logger = logger;

        var meter = meters.Create(TracePropagation.SourceName);
        _handled = meter.CreateCounter<long>("messaging.messages.handled", "{message}", "Messages handled by consumers");
        // DLQ depth is checked every 15 s: messages there need manual investigation (Lab 07 style gauge)
        meter.CreateObservableGauge("messaging.dlq.depth",
            () => new Measurement<long>(Interlocked.Read(ref _deadLetterDepth),
                new KeyValuePair<string, object?>("messaging.destination.name", Queue.DeadLetterQueue)),
            "{message}", "Messages in the dead letter queue");
    }

    protected abstract QueueDefinition Queue { get; }

    protected void Handle<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
        => _handlers[typeof(TEvent).Name] = new Registration(
            body => JsonSerializer.Deserialize<TEvent>(body.Span, JsonDefaults.Options) ?? throw new JsonException("Empty message"),
            (sp, e, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)e, ct));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await Topology.DeclareAsync(_channel, Queue, stoppingToken);

        // at most 10 unacknowledged messages at once for this consumer
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, ea) => OnMessageAsync(ea, stoppingToken);
        await _channel.BasicConsumeAsync(Queue.Queue, autoAck: false, consumer, stoppingToken);
        _logger.LogInformation("Consuming {Queue}, bindings {Bindings}, DLQ {DeadLetterQueue}",
            Queue.Queue, string.Join(", ", Queue.Bindings), Queue.DeadLetterQueue);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _deadLetterDepth, await _channel.MessageCountAsync(Queue.DeadLetterQueue, stoppingToken));
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal stop of the application
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is { IsOpen: true })
            await _channel.CloseAsync(cancellationToken);   // unacknowledged messages go back to the queue
        _logger.LogInformation("Consumer of {Queue} stopped", Queue.Queue);
    }

    private async Task OnMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        using var activity = TracePropagation.StartConsumerActivity(ea, Queue.Queue);
        var props = ea.BasicProperties;
        var eventType = props.Type ?? "unknown";
        var attempt = ReadRetryCount(props);

        // These properties go to every log line of the processing, also to the logs of the handler
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = props.MessageId,
            ["EventType"] = eventType,
            ["CorrelationId"] = ReadHeader(props, "x-correlation-id"),
            ["Queue"] = Queue.Queue,
            ["Attempt"] = attempt
        });

        try
        {
            if (!_handlers.TryGetValue(eventType, out var registration))
                throw new PermanentEventException($"No handler for event type '{eventType}'");

            var version = int.TryParse(ReadHeader(props, "x-schema-version"), out var v) ? v : IntegrationEvent.SchemaVersion;
            if (version > IntegrationEvent.SchemaVersion)
                throw new PermanentEventException($"Schema version {version} is newer than supported {IntegrationEvent.SchemaVersion}");

            var @event = registration.Deserialize(ea.Body);

            await using (var messageScope = _scopes.CreateAsyncScope())
                await registration.Invoke(messageScope.ServiceProvider, @event, ct);

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            Count("ack");
            _logger.LogInformation("Event {EventType} {EventId} processed from {Queue}: ack", eventType, props.MessageId, Queue.Queue);
        }
        catch (Exception ex) when (ex is JsonException or PermanentEventException)
        {
            _logger.LogError(ex, "Event {EventType} {EventId} from {Queue} cannot be processed, sent to DLQ without retry",
                eventType, props.MessageId, Queue.Queue);
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            Count("dead_letter");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // the application stops: no ack, RabbitMQ delivers the message again after the restart
        }
        catch (Exception ex)
        {
            await RetryOrDeadLetterAsync(ea, attempt, ex, ct);
        }
    }

    private async Task RetryOrDeadLetterAsync(BasicDeliverEventArgs ea, int attempt, Exception ex, CancellationToken ct)
    {
        var props = ea.BasicProperties;
        if (attempt >= MaxRetries)
        {
            _logger.LogError(ex, "Event {EventType} {EventId} failed after {Attempts} retries, sent to DLQ",
                props.Type, props.MessageId, attempt);
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            Count("dead_letter");
            return;
        }

        // A message returned with requeue: true comes back unchanged, its headers cannot be changed.
        // So the copy with the increased counter is published and the original is acked.
        // The default exchange with the name of the queue as the key: the retry reaches only this consumer,
        // the other services that received the event are not affected.
        await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);    // a short pause for a temporary failure

        var retry = new BasicProperties(props)
        {
            Headers = new Dictionary<string, object?>(props.Headers ?? new Dictionary<string, object?>())
            {
                [RetryHeader] = attempt + 1
            }
        };
        await _channel!.BasicPublishAsync(exchange: "", routingKey: Queue.Queue, mandatory: false, retry, ea.Body, ct);
        await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        Count("retry");

        _logger.LogWarning(ex, "Event {EventType} {EventId} failed, retry {Attempt} of {MaxRetries}",
            props.Type, props.MessageId, attempt + 1, MaxRetries);
    }

    private void Count(string result) => _handled.Add(1,
        new KeyValuePair<string, object?>("messaging.destination.name", Queue.Queue),
        new KeyValuePair<string, object?>("result", result));

    private static int ReadRetryCount(IReadOnlyBasicProperties props)
        => props.Headers is not null && props.Headers.TryGetValue(RetryHeader, out var value) && value is not null
            ? Convert.ToInt32(value)
            : 0;

    private static string? ReadHeader(IReadOnlyBasicProperties props, string name)
        => props.Headers is not null && props.Headers.TryGetValue(name, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                null => null,
                _ => value.ToString()
            }
            : null;

    private sealed record Registration(
        Func<ReadOnlyMemory<byte>, IntegrationEvent> Deserialize,
        Func<IServiceProvider, IntegrationEvent, CancellationToken, Task> Invoke);
}
