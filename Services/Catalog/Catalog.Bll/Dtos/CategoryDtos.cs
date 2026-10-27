namespace Catalog.Bll.Dtos;

public record CategoryDto(int Id, string Name);

public record CategoryEventDto(int EventId, string Title, DateTime StartsAt, DateTime AddedAt);

public record CategoryWithEventsDto(int Id, string Name, IReadOnlyList<CategoryEventDto> Events);
