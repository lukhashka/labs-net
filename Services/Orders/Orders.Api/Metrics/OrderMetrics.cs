using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Orders.Api.Metrics;

// Business metrics of the Orders service. Singleton: the instruments live as long as the application.
// The meter is added to OpenTelemetry in ServiceDefaults (AddMeter("Orders.Api")).
//
// Tags and their values (the same names in all services, few values, no ids):
//   order.status - Submitted, StockConfirmed, Paid, Shipped, Cancelled
//   operation    - create, confirm, change-status, cancel, pay, issue-tickets, delete
//   status       - success, failure
//   error.type   - name of the exception class, only for failures
public sealed class OrderMetrics
{
    public const string MeterName = "Orders.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _ticketsOrdered;
    private readonly Counter<long> _statusChanged;
    private readonly Histogram<double> _duration;

    public OrderMetrics(IMeterFactory meterFactory, OrderStatusSnapshot snapshot)
    {
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>(
            "orders.created", unit: "{order}", description: "Number of created orders");
        _ticketsOrdered = meter.CreateCounter<long>(
            "orders.tickets.ordered", unit: "{ticket}", description: "Number of tickets in created orders");
        _statusChanged = meter.CreateCounter<long>(
            "orders.status.changed", unit: "{order}", description: "Orders that moved to a new status");
        _duration = meter.CreateHistogram<double>(
            "orders.operation.duration", unit: "ms", description: "Duration of order operations");

        // Gauge: a snapshot of the current state, read at the moment the metrics are collected
        meter.CreateObservableGauge(
            "orders.current", snapshot.Observe, unit: "{order}", description: "Orders in each status right now");
    }

    // Called right after the operation succeeded
    public void OrderCreated(string status, int tickets)
    {
        var tag = new KeyValuePair<string, object?>("order.status", status);
        _created.Add(1, tag);
        _ticketsOrdered.Add(tickets, tag);
    }

    public void StatusChanged(string newStatus) =>
        _statusChanged.Add(1, new KeyValuePair<string, object?>("order.status", newStatus));

    // The duration is recorded for every execution, failed ones too, otherwise the picture is too optimistic
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action();
            Record(operation, stopwatch, error: null);
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
        var tags = new TagList
        {
            { "operation", operation },
            { "status", error is null ? "success" : "failure" }
        };
        if (error is not null)
            tags.Add("error.type", error.GetType().Name);

        _duration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }
}
