using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

// 1:1 with Event — EventId is PK and FK at the same time
public class EventDetail
{
    public int EventId { get; private set; }
    public string Description { get; private set; } = "";
    public string? Organizer { get; private set; }
    public int AgeLimit { get; private set; }

    private EventDetail() { }

    public EventDetail(string description, string? organizer, int ageLimit)
    {
        Update(description, organizer, ageLimit);
    }

    public void Update(string description, string? organizer, int ageLimit)
    {
        if (ageLimit < 0 || ageLimit > 21) throw new DomainException("Age limit must be between 0 and 21");
        Description = description;
        Organizer = organizer;
        AgeLimit = ageLimit;
    }
}
