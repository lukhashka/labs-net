using System.Data;
using System.Diagnostics;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Orders.Bll.Dtos;
using Orders.Dal;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;

namespace Orders.Bll.Services;

public interface IOrderService
{
    Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<OrderSummaryDto>> GetPageAsync(long? customerId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<OrderDto> CreateAsync(CreateOrderDto input, CancellationToken ct = default);
    Task ConfirmOrderAsync(long orderId, CancellationToken ct = default);
    Task ChangeStatusAsync(long orderId, string newStatus, CancellationToken ct = default);
    Task CancelAsync(long orderId, CancellationToken ct = default);
    Task RegisterPaymentAsync(long orderId, RegisterPaymentDto input, CancellationToken ct = default);
    Task IssueTicketsAsync(long orderId, CancellationToken ct = default);
    Task DeleteAsync(long orderId, CancellationToken ct = default);
    Task UpdateDetailsAsync(long orderId, UpdateOrderDetailsDto input, CancellationToken ct = default);
}

public class OrderService(IUnitOfWork uow, IMapper mapper, ILogger<OrderService> logger) : IOrderService
{
    // Own span for the business operation; ServiceDefaults subscribes to the source named as the application
    private static readonly ActivitySource Source = new("Orders.Api");

    public async Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var order = await uow.Orders.GetWithItemsAsync(id, ct) ?? throw new NotFoundException($"Order {id} not found");
        return mapper.Map<OrderDto>(order);
    }

    public async Task<PagedResult<OrderSummaryDto>> GetPageAsync(
        long? customerId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await uow.Orders.GetPageAsync(customerId, status, page, pageSize, ct);
        return new PagedResult<OrderSummaryDto>(mapper.Map<List<OrderSummaryDto>>(items), total, page, pageSize);
    }

    // Order, details and items are written in one transaction
    public async Task<OrderDto> CreateAsync(CreateOrderDto input, CancellationToken ct = default)
    {
        if (input.Items.Select(i => i.TicketTypeId).Distinct().Count() != input.Items.Count)
            throw new ValidationException("The same ticket type is listed more than once");

        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            _ = await uow.Customers.GetByIdAsync(input.CustomerId, ct)
                ?? throw new NotFoundException($"Customer {input.CustomerId} not found");

            var ticketTypes = (await uow.TicketTypes.GetByIdsAsync(input.Items.Select(i => i.TicketTypeId), ct))
                .ToDictionary(t => t.Id);

            var items = new List<OrderItem>();
            foreach (var line in input.Items)
            {
                if (!ticketTypes.TryGetValue(line.TicketTypeId, out var ticketType))
                    throw new NotFoundException($"Ticket type {line.TicketTypeId} not found");
                if (ticketType.Stock < line.Units)
                    throw new BusinessConflictException($"Only {ticketType.Stock} tickets of type {ticketType.Id} are left");

                // Name and price are taken from TicketTypes and stored as a snapshot (duplication policy, Lab 01)
                items.Add(new OrderItem
                {
                    TicketTypeId = ticketType.Id,
                    EventTitle = ticketType.EventTitle,
                    TicketTypeName = ticketType.Name,
                    UnitPrice = ticketType.Price,
                    Units = line.Units
                });
            }

            var order = new Order
            {
                CustomerId = input.CustomerId,
                Status = OrderStatus.Submitted,
                TotalAmount = items.Sum(i => i.UnitPrice * i.Units)
            };
            order.Id = await uow.Orders.AddAsync(order, ct);

            await uow.Orders.AddDetailsAsync(new OrderDetails
            {
                OrderId = order.Id,
                AttendeeName = input.AttendeeName,
                AttendeeEmail = input.AttendeeEmail,
                Comment = input.Comment
            }, ct);

            foreach (var item in items)
            {
                item.OrderId = order.Id;
                await uow.Orders.AddItemAsync(item, ct);
            }

            await uow.CommitAsync(ct);
            logger.LogInformation("Order {OrderId} created for customer {CustomerId}, {ItemsCount} items, amount {Amount}",
                order.Id, order.CustomerId, items.Count, order.TotalAmount);
            return await GetByIdAsync(order.Id, ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // Two repositories, one transaction: the status changes and tickets are written off together or not at all
    public async Task ConfirmOrderAsync(long orderId, CancellationToken ct = default)
    {
        using var activity = Source.StartActivity("ConfirmOrder");
        activity?.SetTag("order.id", orderId);

        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var order = await uow.Orders.GetWithItemsAsync(orderId, ct)
                        ?? throw new NotFoundException($"Order {orderId} not found");

            if (order.Items.Count == 0)
                throw new BusinessConflictException($"Order {orderId} has no items");

            // -> StockConfirmed; the procedure from Lab 01 checks that the transition is allowed
            await uow.Orders.ConfirmAsync(orderId, null, ct);

            // not enough tickets -> BusinessConflictException -> rollback, the status goes back
            foreach (var item in order.Items)
                await uow.TicketTypes.DecreaseStockAsync(item.TicketTypeId, item.Units, ct);

            await uow.CommitAsync(ct);
            logger.LogInformation("Order {OrderId} confirmed, {ItemsCount} items, status {Status}",
                orderId, order.Items.Count, OrderStatus.StockConfirmed);
        }
        catch (Exception ex)
        {
            await uow.RollbackAsync(CancellationToken.None);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning("Order {OrderId} was not confirmed: {Reason}", orderId, ex.Message);
            throw;
        }
    }

    public async Task ChangeStatusAsync(long orderId, string newStatus, CancellationToken ct = default)
    {
        await uow.Orders.ChangeStatusAsync(orderId, newStatus, null, ct);
        logger.LogInformation("Order {OrderId} moved to status {Status}", orderId, newStatus);
    }

    public async Task CancelAsync(long orderId, CancellationToken ct = default)
    {
        await uow.Orders.CancelAsync(orderId, null, ct);
        logger.LogInformation("Order {OrderId} cancelled", orderId);
    }

    public async Task RegisterPaymentAsync(long orderId, RegisterPaymentDto input, CancellationToken ct = default)
    {
        await uow.Orders.RegisterPaymentAsync(orderId, input.Amount, input.Method, input.ExternalRef, ct);
        logger.LogInformation("Payment registered for order {OrderId}, amount {Amount}, method {Method}",
            orderId, input.Amount, input.Method);
    }

    public async Task IssueTicketsAsync(long orderId, CancellationToken ct = default)
    {
        await uow.Orders.IssueTicketsAsync(orderId, ct);
        logger.LogInformation("Tickets issued for order {OrderId}", orderId);
    }

    // Lab 09: PUT /api/orders/{id}; the details of a cancelled order are not changed any more
    public async Task UpdateDetailsAsync(long orderId, UpdateOrderDetailsDto input, CancellationToken ct = default)
    {
        var order = await uow.Orders.GetWithItemsAsync(orderId, ct) ?? throw new NotFoundException($"Order {orderId} not found");
        if (order.Status == "Cancelled")
            throw new BusinessConflictException($"Order {orderId} is cancelled");

        await uow.Orders.UpdateDetailsAsync(new OrderDetails
        {
            OrderId = orderId, AttendeeName = input.AttendeeName, AttendeeEmail = input.AttendeeEmail, Comment = input.Comment
        }, ct);
    }

    public async Task DeleteAsync(long orderId, CancellationToken ct = default)
    {
        if (!await uow.Orders.DeleteAsync(orderId, ct))
            throw new NotFoundException($"Order {orderId} not found");
    }
}
