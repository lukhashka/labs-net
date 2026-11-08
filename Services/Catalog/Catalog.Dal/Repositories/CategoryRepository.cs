using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

public interface ICategoryRepository : IGenericRepository<Category>
{
    Task<Category?> GetWithEventsAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}

// Explicit Loading: category first, its events — by a separate (second) query only when needed
public class CategoryRepository(CatalogDbContext context) : GenericRepository<Category>(context), ICategoryRepository
{
    public async Task<Category?> GetWithEventsAsync(int id, CancellationToken ct = default)
    {
        var category = await Set.FirstOrDefaultAsync(c => c.Id == id, ct);          // 1st query
        if (category is null) return null;

        await Context.Entry(category)
            .Collection(c => c.EventCategories)
            .Query()
            .Include(ec => ec.Event)
            .LoadAsync(ct);                                                          // 2nd query
        return category;
    }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        => Set.AnyAsync(c => c.Name == name, ct);
}
