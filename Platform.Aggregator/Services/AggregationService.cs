using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Dtos;
using Polly;

namespace Platform.Aggregator.Services;

// The main source of the card is unavailable: nothing to aggregate -> 503
public class RequiredServiceUnavailableException(string service, Exception inner)
    : Exception($"{service} is unavailable", inner);

public class AggregationService(OrdersClient orders, CatalogClient catalog, ReviewsClient reviews, ILogger<AggregationService> logger)
{
    // Order card: order (orders-api) + current event data (catalog-api) + ratings of these events (reviews-api)
    public async Task<FullOrderDto?> GetFullOrderAsync(long orderId, CancellationToken ct)
    {
        var order = await RequiredAsync(() => orders.GetOrderAsync(orderId, ct), "orders-api", ct);
        if (order is null)
            return null;                                    // without an order there is nothing to aggregate

        var warnings = new List<string>();
        var eventIds = order.Items.Select(i => i.EventId).Distinct().ToList();

        // Independent calls run at the same time: total time is the slowest call, not the sum
        var eventsTask = SafeAsync(() => catalog.GetEventsAsync(eventIds, ct), "catalog-api", warnings, ct);
        var ratingsTask = SafeAsync(() => reviews.GetRatingsAsync(eventIds, ct), "reviews-api", warnings, ct);
        await Task.WhenAll(eventsTask, ratingsTask);

        // Consistency check: the card contains only events of this order
        var events = (await eventsTask ?? []).Where(e => eventIds.Contains(e.Id)).ToList();
        if (await eventsTask is not null && events.Count != eventIds.Count)
            lock (warnings) warnings.Add("Some events of the order were not found in catalog-api");

        return new FullOrderDto(order, events, await ratingsTask ?? [], warnings);
    }

    // Event page: event (catalog-api) + available tickets (orders-api) + rating and latest reviews (reviews-api)
    public async Task<FullEventDto?> GetFullEventAsync(long eventId, CancellationToken ct)
    {
        var card = await RequiredAsync(() => catalog.GetEventAsync(eventId, ct), "catalog-api", ct);
        if (card is null)
            return null;

        var warnings = new List<string>();
        var ticketsTask = SafeAsync(() => orders.GetAvailabilityAsync(eventId, ct), "orders-api", warnings, ct);
        var reviewsTask = SafeAsync(async () => (object?)await reviews.GetSummaryAsync(eventId, 5, ct), "reviews-api", warnings, ct);
        await Task.WhenAll(ticketsTask, reviewsTask);

        var summary = await reviewsTask is (EventRatingDto rating, IReadOnlyList<ReviewDto> latest)
            ? (Rating: rating, Latest: latest)
            : (Rating: (EventRatingDto?)null, Latest: (IReadOnlyList<ReviewDto>)[]);

        return new FullEventDto(card, await ticketsTask ?? [], summary.Rating, summary.Latest, warnings);
    }

    private async Task<T?> RequiredAsync<T>(Func<Task<T?>> call, string service, CancellationToken ct) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Calling {Service}", service);
        try
        {
            var result = await call();
            logger.LogInformation("{Service} responded in {Elapsed} ms", service, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (IsUnavailable(ex, ct))
        {
            logger.LogError(ex, "{Service} is unavailable", service);
            throw new RequiredServiceUnavailableException(service, ex);
        }
    }

    // A failure of one service does not break the whole request: its data is empty and there is a warning
    private async Task<T?> SafeAsync<T>(Func<Task<T?>> call, string service, List<string> warnings, CancellationToken ct) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Calling {Service}", service);
        try
        {
            var result = await call();
            logger.LogInformation("{Service} responded in {Elapsed} ms", service, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (IsUnavailable(ex, ct))
        {
            logger.LogWarning(ex, "{Service} is unavailable after {Elapsed} ms", service, stopwatch.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable");
            return null;
        }
    }

    // ExecutionRejectedException: Polly gave up (open circuit breaker, timeout of the resilience pipeline).
    // A cancellation requested by the client is not a failure of the service and goes further.
    private static bool IsUnavailable(Exception ex, CancellationToken ct)
        => ex is HttpRequestException or TaskCanceledException or ExecutionRejectedException && !ct.IsCancellationRequested;
}
