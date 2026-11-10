using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Dtos;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]   // Lab 09: a valid user token
[Route("api/aggregator")]
[Produces("application/json")]
public class AggregatorController(AggregationService aggregation) : ControllerBase
{
    [HttpGet("orders/{id:long}/full")]
    [ProducesResponseType<FullOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<FullOrderDto>> GetFullOrder(long id, CancellationToken ct)
        => RespondAsync(() => aggregation.GetFullOrderAsync(id, ct), $"Order {id} not found");

    [HttpGet("events/{id:long}/full")]
    [ProducesResponseType<FullEventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<FullEventDto>> GetFullEvent(long id, CancellationToken ct)
        => RespondAsync(() => aggregation.GetFullEventAsync(id, ct), $"Event {id} not found");

    private async Task<ActionResult<T>> RespondAsync<T>(Func<Task<T?>> call, string notFoundMessage) where T : class
    {
        try
        {
            var result = await call();
            return result is null ? Problem(notFoundMessage, statusCode: StatusCodes.Status404NotFound, title: "Resource not found") : Ok(result);
        }
        catch (RequiredServiceUnavailableException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service unavailable");
        }
    }
}
