using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;

namespace Catalog.Bll.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<CategoryWithEventsDto> GetWithEventsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<EventDto>> GetRecentlyAddedAsync(int id, int days, CancellationToken ct = default);
}

public class CategoryService(IUnitOfWork uow, IMapper mapper) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct = default)
        => mapper.Map<List<CategoryDto>>(await uow.Categories.GetAllAsync(ct));

    public async Task<CategoryWithEventsDto> GetWithEventsAsync(int id, CancellationToken ct = default)
    {
        var category = await uow.Categories.GetWithEventsAsync(id, ct)          // explicit loading
                       ?? throw new EntityNotFoundException(nameof(Category), id);
        return mapper.Map<CategoryWithEventsDto>(category);
    }

    public async Task<IReadOnlyList<EventDto>> GetRecentlyAddedAsync(int id, int days, CancellationToken ct = default)
    {
        _ = await uow.Categories.GetByIdAsync(id, ct) ?? throw new EntityNotFoundException(nameof(Category), id);
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var events = await uow.EventCategories.GetRecentlyAddedAsync(id, since, ct);   // M:N via AddedAt
        return mapper.Map<List<EventDto>>(events);
    }
}
