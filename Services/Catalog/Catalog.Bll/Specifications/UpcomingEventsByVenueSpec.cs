using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Specifications;

// Second spec with other criteria — works through the same repository.ListAsync(spec)
// without any change in the repository.
public class UpcomingEventsByVenueSpec : Specification<Event>
{
    public UpcomingEventsByVenueSpec(int venueId, DateTime now)
    {
        Query.AsNoTracking()
            .Include(e => e.TicketTypes)
            .Where(e => e.VenueId == venueId && e.StartsAt >= now && e.Status != EventStatus.Cancelled)
            .OrderBy(e => e.StartsAt);
    }
}
