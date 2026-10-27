using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Mapping;

public class CatalogProfile : Profile
{
    public CatalogProfile()
    {
        CreateMap<TicketType, TicketTypeDto>();
        CreateMap<Category, CategoryDto>();

        CreateMap<Event, EventDto>()
            .ForCtorParam(nameof(EventDto.Status), o => o.MapFrom(e => e.Status.ToString()))
            .ForCtorParam(nameof(EventDto.MinPrice),
                o => o.MapFrom(e => e.TicketTypes.Count == 0 ? (decimal?)null : e.TicketTypes.Min(t => t.Price)));

        CreateMap<Event, EventDetailsDto>()
            .ForCtorParam(nameof(EventDetailsDto.Status), o => o.MapFrom(e => e.Status.ToString()))
            .ForCtorParam(nameof(EventDetailsDto.VenueName), o => o.MapFrom(e => e.Venue != null ? e.Venue.Name : null))
            .ForCtorParam(nameof(EventDetailsDto.Description), o => o.MapFrom(e => e.Detail != null ? e.Detail.Description : null))
            .ForCtorParam(nameof(EventDetailsDto.Organizer), o => o.MapFrom(e => e.Detail != null ? e.Detail.Organizer : null))
            .ForCtorParam(nameof(EventDetailsDto.AgeLimit), o => o.MapFrom(e => e.Detail != null ? e.Detail.AgeLimit : (int?)null))
            .ForCtorParam(nameof(EventDetailsDto.Categories),
                o => o.MapFrom(e => e.EventCategories.Select(ec => ec.Category.Name).ToList()));

        CreateMap<EventCategory, CategoryEventDto>()
            .ForCtorParam(nameof(CategoryEventDto.Title), o => o.MapFrom(ec => ec.Event.Title))
            .ForCtorParam(nameof(CategoryEventDto.StartsAt), o => o.MapFrom(ec => ec.Event.StartsAt));

        CreateMap<Category, CategoryWithEventsDto>()
            .ForCtorParam(nameof(CategoryWithEventsDto.Events),
                o => o.MapFrom(c => c.EventCategories.OrderByDescending(ec => ec.AddedAt)));
    }
}
