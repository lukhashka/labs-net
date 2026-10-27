using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/ticket-types")]
[Produces("application/json")]
public class TicketTypesController(ITicketTypeService service) : ControllerBase
{
    // GET api/ticket-types?page=2&pageSize=5&minPrice=20&sortBy=price&sortDir=desc
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketTypeDto>>> GetList([FromQuery] TicketTypeQuery query, CancellationToken ct)
    {
        var result = await service.GetListAsync(query, ct);
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketTypeDto>> GetById(int id, CancellationToken ct)
        => Ok(await service.GetByIdAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketTypeDto>> Create(
        CreateTicketTypeDto input, [FromServices] IValidator<CreateTicketTypeDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var created = await service.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}/price")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketTypeDto>> ChangePrice(
        int id, ChangePriceDto input, [FromServices] IValidator<ChangePriceDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        return Ok(await service.ChangePriceAsync(id, input.Price, ct));
    }

    [HttpPost("{id:int}/stock/remove")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketTypeDto>> RemoveStock(int id, StockChangeDto input, CancellationToken ct)
        => Ok(await service.RemoveStockAsync(id, input.Quantity, ct));

    [HttpPost("{id:int}/stock/add")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketTypeDto>> AddStock(int id, StockChangeDto input, CancellationToken ct)
        => Ok(await service.AddStockAsync(id, input.Quantity, ct));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
