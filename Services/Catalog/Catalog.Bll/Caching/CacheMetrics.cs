using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Catalog.Bll.Caching;

// The meter is registered in ServiceDefaults: .AddMeter("Catalog.Cache")
public class CacheMetrics
{
    private readonly Counter<long> _hits, _misses, _invalidations, _evictions;
    private readonly Histogram<double> _redisDuration;
    private long _l1Hits, _l1Misses, _l2Hits, _l2Misses;

    public CacheMetrics(IMeterFactory meterFactory, IMemoryCache memory)
    {
        var meter = meterFactory.Create("Catalog.Cache");
        _hits = meter.CreateCounter<long>("cache.hits");
        _misses = meter.CreateCounter<long>("cache.misses");
        _invalidations = meter.CreateCounter<long>("cache.invalidations");
        _evictions = meter.CreateCounter<long>("cache.evictions");
        _redisDuration = meter.CreateHistogram<double>("cache.redis.duration", unit: "ms");

        meter.CreateObservableGauge("cache.hit_ratio", () => new[]
        {
            new Measurement<double>(Ratio(Interlocked.Read(ref _l1Hits), Interlocked.Read(ref _l1Misses)), new KeyValuePair<string, object?>("cache.level", "L1")),
            new Measurement<double>(Ratio(Interlocked.Read(ref _l2Hits), Interlocked.Read(ref _l2Misses)), new KeyValuePair<string, object?>("cache.level", "L2"))
        });
        meter.CreateObservableGauge("cache.l1.entries", () => memory.GetCurrentStatistics()?.CurrentEntryCount ?? 0);
    }

    public void Hit(string level)
    {
        _hits.Add(1, new KeyValuePair<string, object?>("cache.level", level));
        if (level == "L1") Interlocked.Increment(ref _l1Hits); else Interlocked.Increment(ref _l2Hits);
    }

    public void Miss(string level)
    {
        _misses.Add(1, new KeyValuePair<string, object?>("cache.level", level));
        if (level == "L1") Interlocked.Increment(ref _l1Misses); else Interlocked.Increment(ref _l2Misses);
    }

    public void Invalidated() => _invalidations.Add(1);

    public void Evicted(string reason) => _evictions.Add(1, new KeyValuePair<string, object?>("cache.eviction.reason", reason));

    public void RecordRedisDuration(double milliseconds, string operation)
        => _redisDuration.Record(milliseconds, new KeyValuePair<string, object?>("cache.operation", operation));

    public (double L1, double L2) HitRatios()
        => (Ratio(Interlocked.Read(ref _l1Hits), Interlocked.Read(ref _l1Misses)), Ratio(Interlocked.Read(ref _l2Hits), Interlocked.Read(ref _l2Misses)));

    private static double Ratio(long hits, long misses) => hits + misses == 0 ? 0 : (double)hits / (hits + misses);
}
