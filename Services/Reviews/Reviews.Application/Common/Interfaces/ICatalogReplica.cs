namespace Reviews.Application.Common.Interfaces;

// Lab 08: the denormalized copy of Catalog data in ReviewsDb (eventTitle of reviews and discussions)
// and the bookkeeping that makes the event consumer idempotent. Implemented in Infrastructure.
public interface ICatalogReplica
{
    Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct);

    // the time of the last applied rename of the event, null if the title was never synchronized
    Task<DateTime?> GetTitleChangedAtAsync(long catalogEventId, CancellationToken ct);

    // sets eventTitle in every review and discussion of the event; returns the numbers of updated documents
    Task<(long Reviews, long Discussions)> SetEventTitleAsync(long catalogEventId, string title, CancellationToken ct);

    Task SaveTitleChangedAtAsync(long catalogEventId, DateTime changedAt, CancellationToken ct);

    Task MarkProcessedAsync(Guid eventId, CancellationToken ct);
}
