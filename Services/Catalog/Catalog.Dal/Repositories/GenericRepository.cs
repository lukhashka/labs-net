using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

// Async Generic Repository on DbSet<T>. It never calls SaveChanges — that is the job of UnitOfWork.
public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly CatalogDbContext Context;
    protected readonly DbSet<T> Set;

    public GenericRepository(CatalogDbContext context)
    {
        Context = context;
        Set = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => await Set.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await Set.AddAsync(entity, ct);

    // Update/Delete only change the state in the change tracker, so they are synchronous
    public void Update(T entity) => Set.Update(entity);

    public void Delete(T entity) => Set.Remove(entity);

    public async Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct = default)
        => await Set.WithSpecification(spec).ToListAsync(ct);

    // count ignores paging/ordering of the spec, only Where criteria are applied
    public async Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default)
        => await SpecificationEvaluator.Default.GetQuery(Set.AsQueryable(), spec, evaluateCriteriaOnly: true)
            .CountAsync(ct);
}
