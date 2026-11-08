using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

// Analog of CatalogItem from eShop: Code ~ SKU, stock is changed only by methods
public class TicketType
{
    public int Id { get; private set; }
    public int EventId { get; private set; }
    public Event? Event { get; private set; }
    public string Name { get; private set; } = "";
    public string Code { get; private set; } = "";
    public decimal Price { get; private set; }
    public int Capacity { get; private set; }
    public int AvailableStock { get; private set; }

    private TicketType() { }

    public TicketType(int eventId, string name, string code, decimal price, int capacity)
    {
        if (price <= 0) throw new DomainException("Price must be positive");
        if (capacity <= 0) throw new DomainException("Capacity must be positive");
        EventId = eventId;
        Name = name;
        Code = code;
        Price = price;
        Capacity = capacity;
        AvailableStock = capacity;
    }

    public void ChangePrice(decimal newPrice)
    {
        if (newPrice <= 0) throw new DomainException("Price must be positive");
        Price = newPrice;
    }

    public void RemoveStock(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        if (AvailableStock < quantity) throw new DomainException($"Only {AvailableStock} tickets available");
        AvailableStock -= quantity;
    }

    public void AddStock(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        if (AvailableStock + quantity > Capacity) throw new DomainException($"Stock can't exceed capacity {Capacity}");
        AvailableStock += quantity;
    }
}
