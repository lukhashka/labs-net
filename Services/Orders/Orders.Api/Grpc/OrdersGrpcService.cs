using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Orders.Bll.Services;
using Orders.Domain.Exceptions;
using Orders.Grpc;

namespace Orders.Api.GrpcServices;

// Adapter over the BLL of Lab 02: conversion of models and exceptions only
public class OrdersGrpcService(IOrderService orders, ITicketTypeService ticketTypes) : OrdersService.OrdersServiceBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Id must be positive"));

        Activity.Current?.SetTag("order.id", request.Id);
        try
        {
            var order = await orders.GetByIdAsync(request.Id, context.CancellationToken);
            return new OrderReply
            {
                Id = order.Id,
                CustomerId = order.CustomerId,
                Status = order.Status,
                OrderDate = Timestamp.FromDateTime(DateTime.SpecifyKind(order.OrderDate, DateTimeKind.Utc)),
                TotalAmount = (double)order.TotalAmount,
                Items =
                {
                    order.Items.Select(i => new OrderItemReply
                    {
                        TicketTypeId = i.TicketTypeId,
                        EventId = i.EventId,
                        EventTitle = i.EventTitle,
                        TicketTypeName = i.TicketTypeName,
                        UnitPrice = (double)i.UnitPrice,
                        Units = i.Units
                    })
                }
            };
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<AvailabilityReply> GetAvailability(GetAvailabilityRequest request, ServerCallContext context)
    {
        if (request.EventId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "EventId must be positive"));

        var items = await ticketTypes.GetAllAsync(request.EventId, context.CancellationToken);
        return new AvailabilityReply
        {
            Tickets = { items.Select(t => new TicketAvailability { Id = t.Id, Name = t.Name, Price = (double)t.Price, Stock = t.Stock }) }
        };
    }
}
