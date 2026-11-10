using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.EventBus.Events;
using RabbitMQ.Client;

namespace Platform.EventBus;

// A message ready to go to the broker. The outbox stores exactly these fields.
public sealed record OutgoingMessage(
    Guid MessageId, string EventType, string Exchange, string RoutingKey, ReadOnlyMemory<byte> Body,
    string? CorrelationId, DateTime OccurredAt, ActivityContext Parent = default);

public interface IEventPublisher
{
    Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct) where T : IntegrationEvent;
    Task PublishAsync(OutgoingMessage message, CancellationToken ct);
}

// Publishes events as persistent JSON messages. Publisher confirms are on: PublishAsync returns only after
// the broker has accepted the message, so the outbox marks a row as published only when it is really in RabbitMQ.
public sealed class RabbitMqEventPublisher(IConnection connection, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);   // a channel must not be used by two threads at once
    private IChannel? _channel;

    public Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct) where T : IntegrationEvent
        => PublishAsync(new OutgoingMessage(@event.EventId, typeof(T).Name, exchange, routingKey,
            JsonSerializer.SerializeToUtf8Bytes(@event, JsonDefaults.Options), @event.CorrelationId, @event.OccurredAt), ct);

    public async Task PublishAsync(OutgoingMessage message, CancellationToken ct)
    {
        using var activity = TracePropagation.StartPublishActivity(
            message.Exchange, message.RoutingKey, message.EventType, message.MessageId.ToString(), message.Parent);

        var props = new BasicProperties
        {
            Persistent = true,                                        // written to disk by the broker
            ContentType = "application/json",
            MessageId = message.MessageId.ToString(),                 // = EventId, consumers deduplicate by it
            Type = message.EventType,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["x-correlation-id"] = message.CorrelationId,
                ["x-schema-version"] = IntegrationEvent.SchemaVersion
            }
        };
        TracePropagation.Inject(activity ?? Activity.Current, props.Headers);

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(message.Exchange, message.RoutingKey, mandatory: false, props, message.Body, ct);
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("Published {EventType} {EventId} to {Exchange}/{RoutingKey}, CorrelationId {CorrelationId}",
            message.EventType, message.MessageId, message.Exchange, message.RoutingKey, message.CorrelationId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        _channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);

        // The exchange may not exist yet if no consumer has started; declaring it again is safe
        await _channel.ExchangeDeclareAsync(CatalogEvents.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
    }
}
