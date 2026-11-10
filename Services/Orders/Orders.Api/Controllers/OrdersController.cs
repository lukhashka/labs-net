using Microsoft.AspNetCore.Mvc;
using Orders.Api.Metrics;
using Orders.Bll.Dtos;
using Orders.Bll.Services;

namespace Orders.Api.Controllers;

// Thin adapter: no SQL and no business rules. Errors are converted by DomainExceptionHandler.
// Write operations are measured (orders.operation.duration) and counted after success (Lab 07).
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController(IOrderService orders, OrderMetrics metrics) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> GetPage(
        [FromQuery] long? customerId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await orders.GetPageAsync(customerId, status, page, pageSize, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetById(long id, CancellationToken ct)
        => Ok(await orders.GetByIdAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderDto input, CancellationToken ct)
    {
        var created = await metrics.MeasureAsync("create", () => orders.CreateAsync(input, ct));
        metrics.OrderCreated(created.Status, created.Items.Sum(i => i.Units));
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:long}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("confirm", () => orders.ConfirmOrderAsync(id, ct));
        metrics.StatusChanged("StockConfirmed");
        return NoContent();
    }

    [HttpPost("{id:long}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(long id, ChangeStatusDto input, CancellationToken ct)
    {
        await metrics.MeasureAsync("change-status", () => orders.ChangeStatusAsync(id, input.Status, ct));
        metrics.StatusChanged(input.Status);
        return NoContent();
    }

    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("cancel", () => orders.CancelAsync(id, ct));
        metrics.StatusChanged("Cancelled");
        return NoContent();
    }

    [HttpPost("{id:long}/payments")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterPayment(long id, RegisterPaymentDto input, CancellationToken ct)
    {
        await metrics.MeasureAsync("pay", () => orders.RegisterPaymentAsync(id, input, ct));
        metrics.StatusChanged("Paid");
        return NoContent();
    }

    [HttpPost("{id:long}/tickets")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueTickets(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("issue-tickets", () => orders.IssueTicketsAsync(id, ct));
        metrics.StatusChanged("Shipped");
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("delete", () => orders.DeleteAsync(id, ct));
        return NoContent();
    }
}
