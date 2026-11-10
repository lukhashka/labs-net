using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Platform.Aggregator.Services;

// Composite answers in Redis. The TTL is shorter than the TTL of the services caches,
// otherwise the card would look fresher than the data it was built from.
//
// Graceful degradation (Lab 07): the cache is optional. A Redis failure is caught and logged, the Aggregator
// calls the services directly. After a failure Redis is skipped for 30 s, so the answers do not wait
// for a Redis timeout on every request; the health check "redis-cache" shows Degraded meanwhile.
public class AggregateCache(IDistributedCache cache, IMeterFactory meterFactory, IConfiguration configuration, ILogger<AggregateCache> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RedisPause = TimeSpan.FromSeconds(30);

    private readonly bool _enabled = configuration.GetValue("Caching:Enabled", true);
    private readonly Meter _meter = meterFactory.Create("Aggregator.Cache");
    private Counter<long>? _hits, _misses;
    private long _redisPausedUntilTicks;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (!_enabled || IsRedisPaused())
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
            PauseRedis(ex, key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class
    {
        if (!_enabled || IsRedisPaused())
            return;

        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PauseRedis(ex, key);
        }
    }

    private bool IsRedisPaused() => DateTime.UtcNow.Ticks < Interlocked.Read(ref _redisPausedUntilTicks);

    private void PauseRedis(Exception ex, string key)
    {
        Interlocked.Exchange(ref _redisPausedUntilTicks, (DateTime.UtcNow + RedisPause).Ticks);
        logger.LogWarning("Redis is unavailable for {Key}, aggregating from the services without cache for {PauseSeconds} s: {Reason}",
            key, RedisPause.TotalSeconds, ex.Message);
    }
}
