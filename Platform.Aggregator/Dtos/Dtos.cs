namespace Platform.Aggregator.Dtos;

// Composite DTOs describe business scenarios (order card, event page), they do not copy the structures of services

public record OrderItemDto(long TicketTypeId, long EventId, string EventTitle, string TicketTypeName, decimal UnitPrice, int Units);

public record OrderDto(long Id, long CustomerId, string Status, DateTime OrderDate, decimal TotalAmount, IReadOnlyList<OrderItemDto> Items);

public record EventCardDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, string Status, string? VenueName, string? Description);

public record TicketAvailabilityDto(long Id, string Name, decimal Price, int Stock);

public record EventRatingDto(long EventId, double Average, long Count);

public record ReviewDto(string AuthorName, int Rating, string Text, DateTime CreatedAt);

public record FullOrderDto(
    OrderDto Order,
    IReadOnlyList<EventCardDto> Events,
    IReadOnlyList<EventRatingDto> Ratings,
    IReadOnlyList<string> Warnings);

public record FullEventDto(
    EventCardDto Event,
    IReadOnlyList<TicketAvailabilityDto> Tickets,
    EventRatingDto? Rating,
    IReadOnlyList<ReviewDto> LatestReviews,
    IReadOnlyList<string> Warnings);
