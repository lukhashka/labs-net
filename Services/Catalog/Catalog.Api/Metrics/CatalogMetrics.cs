using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Catalog.Api.Metrics;

// Business metrics of the Catalog service (the cache has its own meter Catalog.Cache from Lab 06).
// Singleton; the meter is added in ServiceDefaults (AddMeter("Catalog.Api")).
//
// Tags: operation (create-event, reschedule, publish, change-price, ...), status (success / failure),
// error.type for failures, event.status (Draft, Published). No ids of events or users.
public sealed class CatalogMetrics
{
    public const string MeterName = "Catalog.Api";

    private readonly Counter<long> _eventsCreated;
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _pricesChanged;
    private readonly Histogram<double> _duration;

    public CatalogMetrics(IMeterFactory meterFactory, IMemoryCache memory)
    {
        var meter = meterFactory.Create(MeterName);

        _eventsCreated = meter.CreateCounter<long>(
            "products.created", unit: "{event}", description: "Events created in the catalog");
        _eventsPublished = meter.CreateCounter<long>(
            "products.published", unit: "{event}", description: "Events published for sale");
        _pricesChanged = meter.CreateCounter<long>(
            "products.price_changed", unit: "{ticket_type}", description: "Price changes of ticket types");
        _duration = meter.CreateHistogram<double>(
            "products.operation.duration", unit: "ms", description: "Duration of catalog write operations");

        // Gauge: entries in the L1 cache at the moment of collection (TrackStatistics is on in Program.cs)
        meter.CreateObservableGauge("products.cache.entries",
            () => memory.GetCurrentStatistics()?.CurrentEntryCount ?? 0,
            unit: "{entry}", description: "Entries in the in-memory cache (L1)");
    }

    public void EventCreated(string status) =>
        _eventsCreated.Add(1, new KeyValuePair<string, object?>("event.status", status));

    public void EventPublished() => _eventsPublished.Add(1);

    public void PriceChanged() => _pricesChanged.Add(1);

    // Duration of successful and failed executions alike
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action();
            Record(operation, stopwatch, null);
            return result;
        }
        catch (Exception ex)
        {
            Record(operation, stopwatch, ex);
            throw;
        }
    }

    public Task MeasureAsync(string operation, Func<Task> action) =>
        MeasureAsync(operation, async () => { await action(); return true; });

    private void Record(string operation, Stopwatch stopwatch, Exception? error)
    {
        var tags = new TagList { { "operation", operation }, { "status", error is null ? "success" : "failure" } };
        if (error is not null)
            tags.Add("error.type", error.GetType().Name);
        _duration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }
}
