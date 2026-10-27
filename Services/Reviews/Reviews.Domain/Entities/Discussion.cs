using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

// Discussion topic of an event
public class Discussion : BaseEntity
{
    public long EventId { get; private set; }
    public string EventTitle { get; private set; } = "";
    public string Title { get; private set; } = "";
    public Author StartedBy { get; private set; } = null!;
    public bool IsClosed { get; private set; }

    private Discussion() { } // for the Mongo serializer

    public Discussion(long eventId, string eventTitle, string title, Author startedBy)
    {
        if (eventId <= 0)
            throw new DomainException("EventId must be positive");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Discussion title is required");

        EventId = eventId;
        EventTitle = eventTitle;
        Title = title;
        StartedBy = startedBy;
    }

    public void Close()
    {
        if (IsClosed)
            throw new DomainException("Discussion is already closed");
        IsClosed = true;
        Touch();
    }
}
