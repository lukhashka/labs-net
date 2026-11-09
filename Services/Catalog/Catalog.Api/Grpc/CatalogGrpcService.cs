using System.Diagnostics;
using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using Catalog.Domain.Exceptions;
using Catalog.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Catalog.Api.GrpcServices;

// Adapter over the BLL, like a controller. IEventService is the same one REST uses, so the cache works for both protocols.
public class CatalogGrpcService(IEventService events, ITicketTypeService ticketTypes) : CatalogService.CatalogServiceBase
{
    public override async Task<EventReply> GetEvent(GetEventRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Id must be positive"));

        Activity.Current?.SetTag("event.id", request.Id);
        try
        {
            return ToReply(await events.GetByIdAsync(request.Id, context.CancellationToken));
        }
        catch (EntityNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<EventsReply> GetEventsByIds(GetEventsByIdsRequest request, ServerCallContext context)
    {
        var reply = new EventsReply();
        foreach (var id in request.Ids.Distinct())
        {
            try
            {
                reply.Events.Add(ToReply(await events.GetByIdAsync(id, context.CancellationToken)));
            }
            catch (EntityNotFoundException)
            {
                // an unknown id is simply absent from the answer
            }
        }
        return reply;
    }

    public override async Task<TicketTypesReply> GetTicketTypesByIds(GetTicketTypesByIdsRequest request, ServerCallContext context)
    {
        var reply = new TicketTypesReply();
        foreach (var id in request.Ids.Distinct())
        {
            try
            {
                var t = await ticketTypes.GetByIdAsync(id, context.CancellationToken);
                reply.TicketTypes.Add(new TicketTypeReply
                {
                    Id = t.Id, EventId = t.EventId, Name = t.Name, Code = t.Code,
                    Price = (double)t.Price, AvailableStock = t.AvailableStock
                });
            }
            catch (EntityNotFoundException)
            {
                // an unknown id is simply absent from the answer
            }
        }
        return reply;
    }

    private static EventReply ToReply(EventDetailsDto e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        StartsAt = ToTimestamp(e.StartsAt),
        EndsAt = ToTimestamp(e.EndsAt),
        Status = e.Status,
        VenueName = e.VenueName ?? "",
        Description = e.Description ?? "",
        Categories = { e.Categories }
    };

    // SQL Server returns DateTime without Kind, all dates of the catalog are in UTC
    private static Timestamp ToTimestamp(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
