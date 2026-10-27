using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Bll.Specifications;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;

namespace Catalog.Bll.Services;

public interface IEventService
{
    Task<PagedResult<EventDto>> GetListAsync(EventQuery query, CancellationToken ct = default);
    Task<EventDetailsDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<EventDto>> GetUpcomingByVenueAsync(int venueId, CancellationToken ct = default);
    Task<EventDetailsDto> CreateAsync(CreateEventDto input, CancellationToken ct = default);
    Task RescheduleAsync(int id, RescheduleEventDto input, CancellationToken ct = default);
    Task PublishAsync(int id, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

// Services work only through IUnitOfWork — no DbContext here
public class EventService(IUnitOfWork uow, IMapper mapper) : IEventService
{
    public async Task<PagedResult<EventDto>> GetListAsync(EventQuery query, CancellationToken ct = default)
    {
        var page = Paging.ClampPage(query.Page);
        var pageSize = Paging.ClampPageSize(query.PageSize);
        var spec = new EventsFilterSpec(query, page, pageSize);

        var total = await uow.Events.CountAsync(spec, ct);
        var items = await uow.Events.ListAsync(spec, ct);
        return new PagedResult<EventDto>(mapper.Map<List<EventDto>>(items), total, page, pageSize);
    }

    public async Task<EventDetailsDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var ev = await uow.Events.GetWithDetailsAsync(id, ct)
                 ?? throw new EntityNotFoundException(nameof(Event), id);
        return mapper.Map<EventDetailsDto>(ev);
    }

    public async Task<IReadOnlyList<EventDto>> GetUpcomingByVenueAsync(int venueId, CancellationToken ct = default)
    {
        _ = await uow.Venues.GetByIdAsync(venueId, ct) ?? throw new EntityNotFoundException(nameof(Venue), venueId);
        var items = await uow.Events.ListAsync(new UpcomingEventsByVenueSpec(venueId, DateTime.UtcNow), ct);
        return mapper.Map<List<EventDto>>(items);
    }

    public async Task<EventDetailsDto> CreateAsync(CreateEventDto input, CancellationToken ct = default)
    {
        // rules that need data from DB are checked by the service
        if (await uow.Events.ExistsBySlugAsync(input.Slug, ct))
            throw new ConflictException($"Event with slug '{input.Slug}' already exists");
        _ = await uow.Venues.GetByIdAsync(input.VenueId, ct)
            ?? throw new EntityNotFoundException(nameof(Venue), input.VenueId);

        // rules of the entity itself are checked by the entity (constructor/methods)
        var ev = new Event(input.Title, input.Slug, input.StartsAt, input.EndsAt, input.VenueId);
        if (!string.IsNullOrWhiteSpace(input.Description))
            ev.SetDetail(input.Description, input.Organizer, input.AgeLimit);

        foreach (var categoryId in input.CategoryIds ?? [])
        {
            _ = await uow.Categories.GetByIdAsync(categoryId, ct)
                ?? throw new EntityNotFoundException(nameof(Category), categoryId);
            ev.AddCategory(categoryId);
        }

        await uow.Events.AddAsync(ev, ct);
        await uow.SaveChangesAsync(ct);                     // one SaveChanges per business operation
        return await GetByIdAsync(ev.Id, ct);
    }

    public async Task RescheduleAsync(int id, RescheduleEventDto input, CancellationToken ct = default)
    {
        var ev = await uow.Events.GetByIdAsync(id, ct) ?? throw new EntityNotFoundException(nameof(Event), id);
        ev.Reschedule(input.StartsAt, input.EndsAt);
        await uow.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(int id, CancellationToken ct = default)
    {
        var ev = await uow.Events.GetWithDetailsAsync(id, ct) ?? throw new EntityNotFoundException(nameof(Event), id);
        ev.Publish();
        await uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var ev = await uow.Events.GetByIdAsync(id, ct) ?? throw new EntityNotFoundException(nameof(Event), id);
        ev.SoftDelete();                                    // audit: IsDeleted instead of physical delete
        await uow.SaveChangesAsync(ct);
    }
}
