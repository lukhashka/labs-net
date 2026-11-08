namespace Catalog.Bll.Dtos;

// Request contracts for collections. PageSize is clamped to MaxPageSize in services.
public record EventQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    int? CategoryId = null,
    DateTime? From = null,
    DateTime? To = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string SortBy = "startsAt",      // startsAt | title | price
    string SortDir = "asc");

public record TicketTypeQuery(
    int Page = 1,
    int PageSize = 20,
    int? EventId = null,
    string? Search = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string SortBy = "name",          // name | price | code
    string SortDir = "asc");

public static class Paging
{
    public const int MaxPageSize = 100;

    public static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    public static int ClampPage(int page) => page < 1 ? 1 : page;
}
