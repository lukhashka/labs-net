using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

public class Category
{
    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public List<EventCategory> EventCategories { get; } = new();   // M:N

    private Category() { }

    public Category(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Category name is required");
        Name = name;
    }
}
