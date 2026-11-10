using System.Diagnostics.Metrics;
using Orders.Dal;

namespace Orders.Api.Metrics;

// Counts of orders per status for the gauge orders.current.
// The gauge callback must be fast and synchronous, so it reads this snapshot and never the database.
public sealed class OrderStatusSnapshot
{
    private volatile IReadOnlyDictionary<string, int> _counts = new Dictionary<string, int>();

    public void Update(IReadOnlyDictionary<string, int> counts) => _counts = counts;

    public IEnumerable<Measurement<int>> Observe() =>
        _counts.Select(c => new Measurement<int>(c.Value, new KeyValuePair<string, object?>("order.status", c.Key)));
}

// Refreshes the snapshot every 15 s with one light GROUP BY query
public sealed class OrderStatusRefresher(
    IServiceScopeFactory scopes, OrderStatusSnapshot snapshot, ILogger<OrderStatusRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                snapshot.Update(await uow.Orders.CountByStatusAsync(stoppingToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // the gauge keeps the last known values; the database state is shown by /health/ready
                logger.LogWarning("Order counts for the gauge were not refreshed: {Reason}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
