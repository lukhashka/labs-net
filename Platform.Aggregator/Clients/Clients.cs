using Catalog.Grpc;
using Grpc.Core;
using Orders.Grpc;
using Platform.Aggregator.Dtos;
using Reviews.Grpc;

namespace Platform.Aggregator.Clients;

// Typed wrappers over the generated gRPC clients: the Aggregator works with its own DTOs.
// Public methods are the same as in the HTTP version from Lab 05.
public class OrdersClient(OrdersService.OrdersServiceClient grpc)
{
    public async Task<OrderDto?> GetOrderAsync(long id, CancellationToken ct)
    {
        try
        {
            var o = await grpc.GetOrderAsync(new GetOrderRequest { Id = id }, cancellationToken: ct);
            var items = o.Items
                .Select(i => new OrderItemDto(i.TicketTypeId, i.EventId, i.EventTitle, i.TicketTypeName, (decimal)i.UnitPrice, i.Units))
                .ToList();
            return new OrderDto(o.Id, o.CustomerId, o.Status, o.OrderDate.ToDateTime(), (decimal)o.TotalAmount, items);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TicketAvailabilityDto>> GetAvailabilityAsync(long eventId, CancellationToken ct)
    {
        var reply = await grpc.GetAvailabilityAsync(new GetAvailabilityRequest { EventId = eventId }, cancellationToken: ct);
        return reply.Tickets.Select(t => new TicketAvailabilityDto(t.Id, t.Name, (decimal)t.Price, t.Stock)).ToList();
    }
}

public class CatalogClient(CatalogService.CatalogServiceClient grpc)
{
    public async Task<EventCardDto?> GetEventAsync(long id, CancellationToken ct)
    {
        try
        {
            return ToDto(await grpc.GetEventAsync(new GetEventRequest { Id = (int)id }, cancellationToken: ct));
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
        {
            return null;
        }
    }

    // One call for all events instead of a call per event as over REST
    public async Task<IReadOnlyList<EventCardDto>> GetEventsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var reply = await grpc.GetEventsByIdsAsync(new GetEventsByIdsRequest { Ids = { ids.Select(i => (int)i) } }, cancellationToken: ct);
        return reply.Events.Select(ToDto).ToList();
    }

    private static EventCardDto ToDto(EventReply e) => new(e.Id, e.Title, e.StartsAt.ToDateTime(), e.EndsAt.ToDateTime(), e.Status,
        e.VenueName.Length == 0 ? null : e.VenueName, e.Description.Length == 0 ? null : e.Description);
}

public class ReviewsClient(ReviewsService.ReviewsServiceClient grpc)
{
    public async Task<IReadOnlyList<EventRatingDto>> GetRatingsAsync(IReadOnlyCollection<long> eventIds, CancellationToken ct)
    {
        var reply = await grpc.GetRatingSummaryAsync(new GetRatingSummaryRequest { EventIds = { eventIds } }, cancellationToken: ct);
        return reply.Ratings.Select(r => new EventRatingDto(r.EventId, r.Average, r.Count)).ToList();
    }

    // Two independent calls over one HTTP/2 connection
    public async Task<(EventRatingDto Rating, IReadOnlyList<ReviewDto> Latest)> GetSummaryAsync(long eventId, int latest, CancellationToken ct)
    {
        var ratingsTask = GetRatingsAsync([eventId], ct);
        var reviewsTask = grpc.GetLatestReviewsAsync(new GetLatestReviewsRequest { EventId = eventId, Limit = latest }, cancellationToken: ct).ResponseAsync;
        await Task.WhenAll(ratingsTask, reviewsTask);

        var rating = (await ratingsTask).FirstOrDefault() ?? new EventRatingDto(eventId, 0, 0);
        var reviews = (await reviewsTask).Reviews
            .Select(r => new ReviewDto(r.AuthorName, r.Rating, r.Text, r.CreatedAt.ToDateTime()))
            .ToList();
        return (rating, reviews);
    }
}
