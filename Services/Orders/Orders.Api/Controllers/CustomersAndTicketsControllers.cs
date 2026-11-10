using Microsoft.AspNetCore.Mvc;
using Orders.Bll.Dtos;
using Orders.Bll.Services;
using Platform.ServiceDefaults.Authentication;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    [RequirePermission("orders:read")]
    [ProducesResponseType<IReadOnlyList<CustomerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerDto>>> GetAll(CancellationToken ct)
        => Ok(await customers.GetAllAsync(ct));

    [HttpGet("{id:long}")]
    [RequirePermission("orders:read")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(long id, CancellationToken ct)
        => Ok(await customers.GetByIdAsync(id, ct));

    [HttpPost]
    [RequirePermission("orders:create")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerDto input, CancellationToken ct)
    {
        var created = await customers.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [RequirePermission("orders:update")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Update(long id, UpdateCustomerDto input, CancellationToken ct)
        => Ok(await customers.UpdateAsync(id, input, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission("orders:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }
}

// Local copy of ticket types: availability of tickets is owned by this service
[ApiController]
[Route("api/tickets")]
[Produces("application/json")]
public class TicketsController(ITicketTypeService ticketTypes) : ControllerBase
{
    [HttpGet]
    [RequirePermission("orders:read")]
    [ProducesResponseType<IReadOnlyList<TicketTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketTypeDto>>> GetAll([FromQuery] long? eventId, CancellationToken ct)
        => Ok(await ticketTypes.GetAllAsync(eventId, ct));

    [HttpGet("{id:long}")]
    [RequirePermission("orders:read")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketTypeDto>> GetById(long id, CancellationToken ct)
        => Ok(await ticketTypes.GetByIdAsync(id, ct));

    [HttpPost]
    [RequirePermission("orders:update")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketTypeDto>> Create(CreateTicketTypeDto input, CancellationToken ct)
    {
        var created = await ticketTypes.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [RequirePermission("orders:update")]
    [ProducesResponseType<TicketTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketTypeDto>> Update(long id, UpdateTicketTypeDto input, CancellationToken ct)
        => Ok(await ticketTypes.UpdateAsync(id, input, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission("orders:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await ticketTypes.DeleteAsync(id, ct);
        return NoContent();
    }
}
