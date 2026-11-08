using AutoMapper;
using Orders.Bll.Dtos;
using Orders.Dal;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;

namespace Orders.Bll.Services;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default);
    Task<CustomerDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public class CustomerService(IUnitOfWork uow, IMapper mapper) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default)
        => mapper.Map<List<CustomerDto>>(await uow.Customers.GetAllAsync(ct));

    public async Task<CustomerDto> GetByIdAsync(long id, CancellationToken ct = default)
        => mapper.Map<CustomerDto>(await uow.Customers.GetByIdAsync(id, ct)
                                   ?? throw new NotFoundException($"Customer {id} not found"));

    public async Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default)
    {
        var id = await uow.Customers.AddAsync(
            new Customer { Email = input.Email, FullName = input.FullName, Phone = input.Phone }, ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto input, CancellationToken ct = default)
    {
        var customer = await uow.Customers.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Customer {id} not found");

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(input.RowVersion);
        }
        catch (FormatException)
        {
            throw new ValidationException("RowVersion is not a valid base64 string");
        }

        customer.FullName = input.FullName;
        customer.Phone = input.Phone;
        customer.RowVersion = rowVersion;

        if (!await uow.Customers.UpdateAsync(customer, ct))
            throw new BusinessConflictException($"Customer {id} was changed by another request");

        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        if (!await uow.Customers.DeleteAsync(id, ct))
            throw new NotFoundException($"Customer {id} not found");
    }
}

public interface ITicketTypeService
{
    Task<IReadOnlyList<TicketTypeDto>> GetAllAsync(long? eventId, CancellationToken ct = default);
    Task<TicketTypeDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<TicketTypeDto> CreateAsync(CreateTicketTypeDto input, CancellationToken ct = default);
    Task<TicketTypeDto> UpdateAsync(long id, UpdateTicketTypeDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public class TicketTypeService(IUnitOfWork uow, IMapper mapper) : ITicketTypeService
{
    public async Task<IReadOnlyList<TicketTypeDto>> GetAllAsync(long? eventId, CancellationToken ct = default)
    {
        var items = eventId is null
            ? await uow.TicketTypes.GetAllAsync(ct)
            : await uow.TicketTypes.GetByEventAsync(eventId.Value, ct);
        return mapper.Map<List<TicketTypeDto>>(items);
    }

    public async Task<TicketTypeDto> GetByIdAsync(long id, CancellationToken ct = default)
        => mapper.Map<TicketTypeDto>(await uow.TicketTypes.GetByIdAsync(id, ct)
                                     ?? throw new NotFoundException($"Ticket type {id} not found"));

    public async Task<TicketTypeDto> CreateAsync(CreateTicketTypeDto input, CancellationToken ct = default)
    {
        await uow.TicketTypes.AddAsync(new TicketType
        {
            Id = input.Id,
            EventId = input.EventId,
            EventTitle = input.EventTitle,
            EventStartsAt = input.EventStartsAt,
            Name = input.Name,
            Price = input.Price,
            Stock = input.Stock
        }, ct);
        return await GetByIdAsync(input.Id, ct);
    }

    // Changes the copy only. Snapshots in OrderItems stay as they were at order time.
    public async Task<TicketTypeDto> UpdateAsync(long id, UpdateTicketTypeDto input, CancellationToken ct = default)
    {
        var ticketType = await uow.TicketTypes.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Ticket type {id} not found");
        ticketType.Name = input.Name;
        ticketType.Price = input.Price;
        await uow.TicketTypes.UpdateAsync(ticketType, ct);
        return mapper.Map<TicketTypeDto>(ticketType);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        if (!await uow.TicketTypes.DeleteAsync(id, ct))
            throw new NotFoundException($"Ticket type {id} not found");
    }
}
