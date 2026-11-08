namespace Catalog.Domain.Entities;

// Explicit M:N join entity with its own field AddedAt
public class EventCategory
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public DateTime AddedAt { get; set; }
}
