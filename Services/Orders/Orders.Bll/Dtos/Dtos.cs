using System.ComponentModel.DataAnnotations;

namespace Orders.Bll.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

// Orders
public record OrderItemDto(long TicketTypeId, long EventId, string EventTitle, string TicketTypeName, decimal UnitPrice, int Units);

public record OrderDetailsDto(string AttendeeName, string AttendeeEmail, string? Comment);

public record OrderDto(long Id, long CustomerId, string Status, DateTime OrderDate, decimal TotalAmount,
    OrderDetailsDto? Details, IReadOnlyList<OrderItemDto> Items);

public record OrderSummaryDto(long Id, string Status, DateTime OrderDate, decimal TotalAmount,
    long CustomerId, string FullName, int TicketsCount);

// Basic validation: the request is rejected before the service is called
public record CreateOrderItemDto(
    [Range(1, long.MaxValue)] long TicketTypeId,
    [Range(1, 1000)] int Units);

public record CreateOrderDto(
    [Range(1, long.MaxValue)] long CustomerId,
    [Required, StringLength(200, MinimumLength = 1)] string AttendeeName,
    [Required, EmailAddress, StringLength(256)] string AttendeeEmail,
    [StringLength(1000)] string? Comment,
    [Required, MinLength(1)] List<CreateOrderItemDto> Items);

public record ChangeStatusDto([Required, StringLength(30)] string Status);

public record RegisterPaymentDto(
    [Range(0.01, 1_000_000)] decimal Amount,
    [Required, StringLength(30)] string Method,
    [Required, StringLength(100)] string ExternalRef);     // idempotency token: repeated value -> 409

// Customers
public record CustomerDto(long Id, string Email, string FullName, string? Phone, string RowVersion);

public record CreateCustomerDto(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(200, MinimumLength = 1)] string FullName,
    [StringLength(30)] string? Phone);

public record UpdateCustomerDto(
    [Required, StringLength(200, MinimumLength = 1)] string FullName,
    [StringLength(30)] string? Phone,
    [Required] string RowVersion);                         // value from the last GET

// Ticket types (local copy)
public record TicketTypeDto(long Id, long EventId, string EventTitle, DateTime EventStartsAt, string Name, decimal Price, int Stock);

public record CreateTicketTypeDto(
    [Range(1, long.MaxValue)] long Id,
    [Range(1, long.MaxValue)] long EventId,
    [Required, StringLength(200)] string EventTitle,
    DateTime EventStartsAt,
    [Required, StringLength(50)] string Name,
    [Range(0.01, 1_000_000)] decimal Price,
    [Range(0, 100_000)] int Stock);

public record UpdateTicketTypeDto(
    [Required, StringLength(50)] string Name,
    [Range(0.01, 1_000_000)] decimal Price);
