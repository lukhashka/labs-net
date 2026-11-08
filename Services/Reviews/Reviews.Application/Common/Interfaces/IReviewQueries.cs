using Reviews.Application.Common.Models;

namespace Reviews.Application.Common.Interfaces;

// Read side of CQRS. Implemented in Infrastructure with Mongo projections,
// so Application never sees IMongoCollection (like IOrderQueries in eShop)
public interface IReviewQueries
{
    Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct);

    Task<PagedResult<ReviewDto>> GetListAsync(
        long? eventId, string? search, int page, int pageSize, CancellationToken ct);
}
