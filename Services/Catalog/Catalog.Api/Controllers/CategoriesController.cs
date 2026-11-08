using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(CancellationToken ct)
        => Ok(await service.GetAllAsync(ct));

    // explicit loading: 2 SQL queries in the log
    [HttpGet("{id:int}/events")]
    [ProducesResponseType<CategoryWithEventsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryWithEventsDto>> GetWithEvents(int id, CancellationToken ct)
        => Ok(await service.GetWithEventsAsync(id, ct));

    // M:N through EventCategory.AddedAt
    [HttpGet("{id:int}/recent-events")]
    [ProducesResponseType<IReadOnlyList<EventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EventDto>>> GetRecentlyAdded(
        int id, [FromQuery] int days = 30, CancellationToken ct = default)
        => Ok(await service.GetRecentlyAddedAsync(id, days, ct));
}
