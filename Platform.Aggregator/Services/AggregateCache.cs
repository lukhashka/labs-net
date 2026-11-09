using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Platform.Aggregator.Services;

// Composite answers in Redis. The TTL is shorter than the TTL of the services caches,
// otherwise the card would look fresher than the data it was built from.
public class AggregateCache(IDistributedCache cache, IMeterFactory meterFactory, IConfiguration configuration, ILogger<AggregateCache> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly bool _enabled = configuration.GetValue("Caching:Enabled", true);
    private readonly Meter _meter = meterFactory.Create("Aggregator.Cache");
    private Counter<long>? _hits, _misses;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (!_enabled)
            return null;

        _hits ??= _meter.CreateCounter<long>("cache.hits");
        _misses ??= _meter.CreateCounter<long>("cache.misses");
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            if (bytes is null)
            {
                _misses.Add(1, new KeyValuePair<string, object?>("cache.level", "aggregate"));
                logger.LogDebug("Cache miss {Key}", key);
                return null;
            }

            _hits.Add(1, new KeyValuePair<string, object?>("cache.level", "aggregate"));
            logger.LogDebug("Cache hit {Key}, {SizeBytes} bytes", key, bytes.Length);
            return JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Redis is unavailable, aggregating without cache: {Reason}", ex.Message);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class
    {
        if (!_enabled)
            return;

        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Redis is unavailable, the answer is not cached: {Reason}", ex.Message);
        }
    }
}
