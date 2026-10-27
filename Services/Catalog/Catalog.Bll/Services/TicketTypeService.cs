using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;

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

public class TicketTypeService(IUnitOfWork uow, IMapper mapper) : ITicketTypeService
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
        _ = await uow.Events.GetByIdAsync(input.EventId, ct)
            ?? throw new EntityNotFoundException(nameof(Event), input.EventId);

        var ticketType = new TicketType(input.EventId, input.Name, input.Code, input.Price, input.Capacity);
        await uow.TicketTypes.AddAsync(ticketType, ct);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task<TicketTypeDto> ChangePriceAsync(int id, decimal price, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        ticketType.ChangePrice(price);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task<TicketTypeDto> RemoveStockAsync(int id, int quantity, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        ticketType.RemoveStock(quantity);                   // DomainException -> 409 if not enough tickets
        await uow.SaveChangesAsync(ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task<TicketTypeDto> AddStockAsync(int id, int quantity, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        ticketType.AddStock(quantity);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var ticketType = await GetEntityAsync(id, ct);
        uow.TicketTypes.Delete(ticketType);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<TicketType> GetEntityAsync(int id, CancellationToken ct)
        => await uow.TicketTypes.GetByIdAsync(id, ct) ?? throw new EntityNotFoundException(nameof(TicketType), id);
}
