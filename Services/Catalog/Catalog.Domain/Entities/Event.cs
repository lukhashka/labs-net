using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

public class Event
{
    public int Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public EventStatus Status { get; private set; } = EventStatus.Draft;
    public string? CoverUrl { get; private set; }
    public int VenueId { get; private set; }
    public Venue? Venue { get; private set; }

    public EventDetail? Detail { get; private set; }                      // 1:1
    public List<TicketType> TicketTypes { get; } = new();                 // 1:N
    public List<EventCategory> EventCategories { get; } = new();          // M:N

    // audit + optimistic concurrency
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Event() { }                                                   // for EF Core

    public Event(string title, string slug, DateTime startsAt, DateTime endsAt, int venueId)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Title is required");
        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException("Slug is required");
        if (endsAt <= startsAt) throw new DomainException("EndsAt must be later than StartsAt");
        Title = title;
        Slug = slug;
        StartsAt = startsAt;
        EndsAt = endsAt;
        VenueId = venueId;
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Title is required");
        Title = title;
        Touch();
    }

    public void Reschedule(DateTime startsAt, DateTime endsAt)
    {
        if (Status == EventStatus.Cancelled) throw new DomainException("Cancelled event can't be rescheduled");
        if (endsAt <= startsAt) throw new DomainException("EndsAt must be later than StartsAt");
        StartsAt = startsAt;
        EndsAt = endsAt;
        Touch();
    }

    public void Publish()
    {
        if (Status != EventStatus.Draft) throw new DomainException($"Only draft event can be published (current: {Status})");
        if (TicketTypes.Count == 0) throw new DomainException("Event without ticket types can't be published");
        Status = EventStatus.Published;
        Touch();
    }

    public void Cancel()
    {
        if (Status == EventStatus.Cancelled) throw new DomainException("Event is already cancelled");
        Status = EventStatus.Cancelled;
        Touch();
    }

    public void SetDetail(string description, string? organizer, int ageLimit)
    {
        if (Detail is null) Detail = new EventDetail(description, organizer, ageLimit);
        else Detail.Update(description, organizer, ageLimit);
        Touch();
    }

    public void AddCategory(int categoryId)
    {
        if (EventCategories.Any(ec => ec.CategoryId == categoryId)) return;
        EventCategories.Add(new EventCategory { CategoryId = categoryId, AddedAt = DateTime.UtcNow });
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
