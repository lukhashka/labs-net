using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

public class Venue
{
    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public int Capacity { get; private set; }
    public List<Event> Events { get; } = new();                 // 1:N

    private Venue() { }                                         // for EF Core

    public Venue(string name, string address, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Venue name is required");
        if (capacity <= 0) throw new DomainException("Capacity must be positive");
        Name = name;
        Address = address;
        Capacity = capacity;
    }
}
