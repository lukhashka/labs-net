using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Caching;

// Key scheme is predictable, so that writes can remove exactly what they changed
public static class CacheKeys
{
    public const string Categories = "catalog:categories";
    public const string EventsFirstPage = "catalog:events:first-page";

    public static string Event(int id) => $"catalog:event:{id}";
}

// L1 is always shorter than L2, otherwise stale data in memory would outlive an update in Redis
public record CachePolicy(TimeSpan L1Absolute, TimeSpan? L1Sliding, TimeSpan L2Absolute);

public static class CachePolicies
{
    public static readonly CachePolicy EventCard = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
    public static readonly CachePolicy EventsFirstPage = new(TimeSpan.FromSeconds(30), null, TimeSpan.FromMinutes(5));
    public static readonly CachePolicy ReferenceData = new(TimeSpan.FromMinutes(5), null, TimeSpan.FromHours(1));
}

public interface ITwoLevelCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CachePolicy policy, CancellationToken ct) where T : class;
    Task RemoveAsync(string key, string reason, CancellationToken ct);
}

// Cache-aside with two levels: L1 (IMemoryCache) -> L2 (Redis) -> database, levels are filled on the way back
public class TwoLevelCache(
    IMemoryCache memory, IDistributedCache redis, CacheMetrics metrics, IConfiguration configuration,
    ILogger<TwoLevelCache> logger) : ITwoLevelCache
{
    // After a Redis failure it is not called for a while: one slow request instead of a timeout in every request
    private static readonly TimeSpan RedisPause = TimeSpan.FromSeconds(30);
    private long _redisPausedUntilTicks;

    // Keys that could not be removed from Redis during an outage; applied when Redis is back
    private readonly ConcurrentDictionary<string, byte> _missedInvalidations = new();

    private readonly bool _enabled = configuration.GetValue("Caching:Enabled", true);

    private sealed record L1Entry(object Value, int SizeBytes, DateTimeOffset ExpiresAt);

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CachePolicy policy, CancellationToken ct) where T : class
    {
        if (!_enabled)
            return await factory();

        if (memory.TryGetValue(key, out L1Entry? l1))
        {
            metrics.Hit("L1");
            logger.LogDebug("Cache hit {Key} level {Level}, {SizeBytes} bytes, expires at {ExpiresAt}", key, "L1", l1!.SizeBytes, l1.ExpiresAt);
            return (T)l1.Value;
        }
        metrics.Miss("L1");

        var bytes = await ReadRedisAsync(key, ct);
        if (bytes is not null)
        {
            metrics.Hit("L2");
            logger.LogDebug("Cache hit {Key} level {Level}, {SizeBytes} bytes", key, "L2", bytes.Length);
            var fromRedis = JsonSerializer.Deserialize<T>(bytes)!;
            SetMemory(key, fromRedis, bytes.Length, policy);
            return fromRedis;
        }
        metrics.Miss("L2");
        logger.LogDebug("Cache miss {Key}, reading the database", key);

        var value = await factory();

        var serialized = JsonSerializer.SerializeToUtf8Bytes(value);
        await WriteRedisAsync(key, serialized, policy, ct);
        SetMemory(key, value, serialized.Length, policy);
        return value;
    }

    // Runs right after SaveChanges: otherwise the next reader could get stale data
    public async Task RemoveAsync(string key, string reason, CancellationToken ct)
    {
        if (!_enabled)
            return;

        memory.Remove(key);
        if (await TryRemoveFromRedisAsync(key, ct))
            _missedInvalidations.TryRemove(key, out _);
        else
            _missedInvalidations[key] = 0;

        metrics.Invalidated();
        logger.LogInformation("Cache invalidated {Key}, reason {Reason}", key, reason);
    }

    private void SetMemory<T>(string key, T value, int sizeBytes, CachePolicy policy) where T : class
    {
        var options = new MemoryCacheEntryOptions
        {
            Size = 1,                                                   // SizeLimit counts entries
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,
            SlidingExpiration = policy.L1Sliding
        };
        options.RegisterPostEvictionCallback((_, _, reason, _) =>
        {
            if (reason is EvictionReason.Capacity or EvictionReason.Expired)
                metrics.Evicted(reason.ToString());
        });

        memory.Set(key, new L1Entry(value, sizeBytes, DateTimeOffset.UtcNow + policy.L1Absolute), options);
    }

    // Redis is an optimization: its failure must make the service slower, not broken
    private async Task<byte[]?> ReadRedisAsync(string key, CancellationToken ct)
    {
        if (IsRedisPaused())
            return null;

        // A write happened during an outage: the old value in Redis must not be trusted again
        if (_missedInvalidations.ContainsKey(key))
        {
            if (await TryRemoveFromRedisAsync(key, ct))
                _missedInvalidations.TryRemove(key, out _);
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var bytes = await redis.GetAsync(key, ct);
            metrics.RecordRedisDuration(stopwatch.Elapsed.TotalMilliseconds, "get");
            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PauseRedis(ex);
            return null;
        }
    }

    private async Task WriteRedisAsync(string key, byte[] bytes, CachePolicy policy, CancellationToken ct)
    {
        if (IsRedisPaused())
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await redis.SetAsync(key, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = policy.L2Absolute
            }, ct);
            metrics.RecordRedisDuration(stopwatch.Elapsed.TotalMilliseconds, "set");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PauseRedis(ex);
        }
    }

    private async Task<bool> TryRemoveFromRedisAsync(string key, CancellationToken ct)
    {
        if (IsRedisPaused())
            return false;

        try
        {
            await redis.RemoveAsync(key, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PauseRedis(ex);
            return false;
        }
    }

    private bool IsRedisPaused() => DateTime.UtcNow.Ticks < Interlocked.Read(ref _redisPausedUntilTicks);

    private void PauseRedis(Exception ex)
    {
        Interlocked.Exchange(ref _redisPausedUntilTicks, (DateTime.UtcNow + RedisPause).Ticks);
        logger.LogWarning("Redis is unavailable, continuing without L2 for {PauseSeconds} s: {Reason}", RedisPause.TotalSeconds, ex.Message);
    }
}
