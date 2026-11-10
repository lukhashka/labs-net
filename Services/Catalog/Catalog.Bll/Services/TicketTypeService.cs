using AutoMapper;
using Catalog.Bll.Caching;
using Catalog.Bll.Dtos;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Platform.EventBus;
using Platform.EventBus.Events;

namespace Catalog.Bll.Services;

public interface ITicketTypeService
{
    Task<PagedResult<TicketTypeDto>> GetListAsync(TicketTypeQuery query, CancellationToken ct = default);
    Task<TicketTypeDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TicketTypeDto> CreateAsync(CreateTicketTypeDto input, CancellationToken ct = default);
    Task<TicketTypeDto> ChangePriceAsync(int id, decimal price, CancellationToken ct = default);
    Task<TicketTypeDto> RemoveStockAsync(int id, int quantity, CancellationToken ct = default);
    Task<TicketTypeDto> AddStockAsync(int id, int quantity, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

// Lab 08: every change of a ticket type that other services copy is written to the outbox in the same
// transaction. If SaveChanges fails, there is no event; the publisher sends it only after the commit.
public class TicketTypeService(IUnitOfWork uow, IMapper mapper, ITwoLevelCache cache,
    ICorrelationIdAccessor correlation, ILogger<TicketTypeService> logger) : ITicketTypeService
{
    public async Task<PagedResult<TicketTypeDto>> GetListAsync(TicketTypeQuery query, CancellationToken ct = default)
    {
        var page = Paging.ClampPage(query.Page);
        var pageSize = Paging.ClampPageSize(query.PageSize);
        var (items, total) = await uow.TicketTypes.GetPagedAsync(page, pageSize, query.EventId, query.Search,
            query.MinPrice, query.MaxPrice, query.SortBy, query.SortDir, ct);
        return new PagedResult<TicketTypeDto>(mapper.Map<List<TicketTypeDto>>(items), total, page, pageSize);
    }

    public async Task<TicketTypeDto> GetByIdAsync(int id, CancellationToken ct = default)
        => mapper.Map<TicketTypeDto>(await GetEntityAsync(id, ct));

    public async Task<TicketTypeDto> CreateAsync(CreateTicketTypeDto input, CancellationToken ct = default)
    {
        if (await uow.TicketTypes.ExistsByCodeAsync(input.Code, ct))
            throw new ConflictException($"Ticket type with code '{input.Code}' already exists");
        var ev = await uow.Events.GetByIdAsync(input.EventId, ct)
            ?? throw new EntityNotFoundException(nameof(Event), input.EventId);

        var ticketType = new TicketType(input.EventId, input.Name, input.Code, input.Price, input.Capacity);

        // The id of the new row is a part of the event, so two SaveChanges in one transaction
        await uow.ExecuteInTransactionAsync(async () =>
        {
            await uow.TicketTypes.AddAsync(ticketType, ct);
            await uow.SaveChangesAsync(ct);
            uow.AddIntegrationEvent(new TicketTypeCreatedEvent(ticketType.Id, ev.Id, ev.Title, ev.StartsAt,
                ticketType.Name, ticketType.Price, ticketType.Capacity) { CorrelationId = correlation.CorrelationId },
                CatalogEvents.TicketTypeCreated);
            await uow.SaveChangesAsync(ct);
        }, ct);
        await InvalidateAsync(ticketType.EventId, "TicketTypeCreated", ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task<TicketTypeDto> ChangePriceAsync(int id, decimal price, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        var oldPrice = ticketType.Price;
        ticketType.ChangePrice(price);                      // invariant in the entity
        uow.AddIntegrationEvent(new TicketTypePriceChangedEvent(id, ticketType.EventId, ticketType.Name, oldPrice, price)
            { CorrelationId = correlation.CorrelationId }, CatalogEvents.TicketTypePriceChanged);
        await uow.SaveChangesAsync(ct);                     // the change and the event in one transaction
        logger.LogInformation("Ticket type {TicketTypeId} price changed to {Price}", id, price);
        await InvalidateAsync(ticketType.EventId, "PriceChanged", ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task<TicketTypeDto> RemoveStockAsync(int id, int quantity, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        ticketType.RemoveStock(quantity);                   // DomainException -> 409 if not enough tickets
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(ticketType.EventId, "StockChanged", ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task<TicketTypeDto> AddStockAsync(int id, int quantity, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        ticketType.AddStock(quantity);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(ticketType.EventId, "StockChanged", ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        uow.TicketTypes.Delete(ticketType);
        uow.AddIntegrationEvent(new TicketTypeDeletedEvent(id, ticketType.EventId) { CorrelationId = correlation.CorrelationId },
            CatalogEvents.TicketTypeDeleted);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(ticketType.EventId, "TicketTypeDeleted", ct);
    }

    // A ticket type is a part of the event card and affects the minimal price on the first page
    private async Task InvalidateAsync(int eventId, string reason, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKeys.Event(eventId), reason, ct);
        await cache.RemoveAsync(CacheKeys.EventsFirstPage, reason, ct);
    }

    private async Task<TicketType> GetEntityAsync(int id, CancellationToken ct)
        => await uow.TicketTypes.GetByIdAsync(id, ct) ?? throw new EntityNotFoundException(nameof(TicketType), id);
}
