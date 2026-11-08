using System.Net;
using Platform.Aggregator.Dtos;

namespace Platform.Aggregator.Clients;

// One typed client per service: it hides the addresses and formats of the service.
// CorrelationId and service discovery come from ServiceDefaults.
public class OrdersClient(HttpClient http)
{
    public async Task<OrderDto?> GetOrderAsync(long id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/orders/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderDto>(ct);
    }

    public async Task<IReadOnlyList<TicketAvailabilityDto>> GetAvailabilityAsync(long eventId, CancellationToken ct)
        => await http.GetFromJsonAsync<List<TicketAvailabilityDto>>($"/api/tickets?eventId={eventId}", ct) ?? [];
}

public class CatalogClient(HttpClient http)
{
    public async Task<EventCardDto?> GetEventAsync(long id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/events/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventCardDto>(ct);
    }

    // The REST API of Lab 03 has no batch read: one parallel call per event
    public async Task<IReadOnlyList<EventCardDto>> GetEventsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var events = await Task.WhenAll(ids.Select(id => GetEventAsync(id, ct)));
        return events.Where(e => e is not null).Select(e => e!).ToList();
    }
}

public class ReviewsClient(HttpClient http)
{
    private record ReviewItem(string AuthorName, int Rating, string Text, DateTime CreatedAt);

    private record ReviewPage(IReadOnlyList<ReviewItem> Items, long TotalCount);

    public async Task<(EventRatingDto Rating, IReadOnlyList<ReviewDto> Latest)> GetSummaryAsync(long eventId, int latest, CancellationToken ct)
    {
        var page = await http.GetFromJsonAsync<ReviewPage>($"/api/reviews?eventId={eventId}&page=1&pageSize=100", ct)
                   ?? new ReviewPage([], 0);

        var average = page.Items.Count == 0 ? 0 : Math.Round(page.Items.Average(r => r.Rating), 2);
        return (new EventRatingDto(eventId, average, page.TotalCount),
            page.Items.Take(latest).Select(r => new ReviewDto(r.AuthorName, r.Rating, r.Text, r.CreatedAt)).ToList());
    }

    public async Task<IReadOnlyList<EventRatingDto>> GetRatingsAsync(IReadOnlyCollection<long> eventIds, CancellationToken ct)
    {
        var summaries = await Task.WhenAll(eventIds.Select(id => GetSummaryAsync(id, 0, ct)));
        return summaries.Select(s => s.Rating).ToList();
    }
}
