using System.ComponentModel.DataAnnotations;

namespace Catalog.Bll.Dtos;

public record EventDto(int Id, string Title, string Slug, DateTime StartsAt, DateTime EndsAt,
    string Status, int VenueId, decimal? MinPrice);

public record EventDetailsDto(int Id, string Title, string Slug, DateTime StartsAt, DateTime EndsAt,
    string Status, string? VenueName, string? Description, string? Organizer, int? AgeLimit,
    IReadOnlyList<TicketTypeDto> TicketTypes, IReadOnlyList<string> Categories);

// simple rules — DataAnnotations; complex rules — CreateEventDtoValidator (FluentValidation)
public record CreateEventDto(
    [Required, StringLength(200)] string Title,
    [Required, StringLength(200)] string Slug,
    DateTime StartsAt,
    DateTime EndsAt,
    [Range(1, int.MaxValue)] int VenueId,
    [StringLength(4000)] string? Description,
    [StringLength(200)] string? Organizer,
    int AgeLimit,
    List<int>? CategoryIds);

public record RescheduleEventDto(DateTime StartsAt, DateTime EndsAt);
