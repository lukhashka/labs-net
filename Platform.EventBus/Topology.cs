using RabbitMQ.Client;

namespace Platform.EventBus;

// A queue of one consumer service and the routing keys it listens to
public sealed record QueueDefinition(string Exchange, string Queue, params string[] Bindings)
{
    public string DeadLetterExchange => Exchange + ".dlx";
    public string DeadLetterQueue => Queue + ".dlq";
}

// Declarative topology, created at start by the code. Declaring the same exchange or queue again is safe;
// changing the arguments of an existing queue by hand in the Management UI would break the next start.
public static class Topology
{
    public static async Task DeclareAsync(IChannel channel, QueueDefinition q, CancellationToken ct)
    {
        // topic: routing by key with patterns ("ticket-type.*", "event.#"); durable: survives a broker restart
        await channel.ExchangeDeclareAsync(q.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);

        // DLX is "direct", the routing key is the name of the source queue. A fanout DLX would copy
        // a dead message of the Orders queue into the DLQ of Reviews too.
        await channel.ExchangeDeclareAsync(q.DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);

        // One queue per consumer service: every service gets its own copy of the event (fan-out)
        await channel.QueueDeclareAsync(q.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = q.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = q.Queue
            },
            cancellationToken: ct);

        foreach (var key in q.Bindings)
            await channel.QueueBindAsync(q.Queue, q.Exchange, routingKey: key, cancellationToken: ct);

        await channel.QueueDeclareAsync(q.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(q.DeadLetterQueue, q.DeadLetterExchange, routingKey: q.Queue, cancellationToken: ct);
    }
}
