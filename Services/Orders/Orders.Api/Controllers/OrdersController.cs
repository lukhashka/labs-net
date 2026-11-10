using Microsoft.AspNetCore.Mvc;
using Orders.Api.Metrics;
using Orders.Api.Security;
using Platform.ServiceDefaults.Authentication;
using Orders.Bll.Dtos;
using Orders.Bll.Services;

namespace Orders.Api.Controllers;

// Thin adapter: no SQL and no business rules. Errors are converted by DomainExceptionHandler.
// Write operations are measured (orders.operation.duration) and counted after success (Lab 07).
// Lab 09: every endpoint needs a permission of the matrix (orders:read ... orders:approve), checked locally.
// A customer (no orders:update) sees and creates only own orders.
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController(IOrderService orders, OrderMetrics metrics, CurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission("orders:read")]
    [ProducesResponseType<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> GetPage(
        [FromQuery] long? customerId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!currentUser.IsStaff)
        {
            var own = await currentUser.CustomerIdAsync(createIfMissing: false, ct);
            if (own is null)
                return Ok(new PagedResult<OrderSummaryDto>([], 0, page, pageSize));
            customerId = own;                            // the filter of the user is ignored: only own orders
        }
        return Ok(await orders.GetPageAsync(customerId, status, page, pageSize, ct));
    }

    [HttpGet("{id:long}")]
    [RequirePermission("orders:read")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetById(long id, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct);
        if (!currentUser.IsStaff && order.CustomerId != await currentUser.CustomerIdAsync(createIfMissing: false, ct))
            return Forbid();
        return Ok(order);
    }

    [HttpPost]
    [RequirePermission("orders:create")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderDto input, CancellationToken ct)
    {
        if (!currentUser.IsStaff)
            input = input with { CustomerId = await currentUser.CustomerIdAsync(createIfMissing: true, ct) ?? input.CustomerId };

        var created = await metrics.MeasureAsync("create", () => orders.CreateAsync(input, ct));
        metrics.OrderCreated(created.Status, created.Items.Sum(i => i.Units));
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [RequirePermission("orders:update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(long id, UpdateOrderDetailsDto input, CancellationToken ct)
    {
        await metrics.MeasureAsync("update", () => orders.UpdateDetailsAsync(id, input, ct));
        return NoContent();
    }

    // "approve" of the matrix: the confirmation of the order (stock is written off)
    [HttpPost("{id:long}/approve")]
    [HttpPost("{id:long}/confirm")]
    [RequirePermission("orders:approve")]
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
    [RequirePermission("orders:update")]
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
    [RequirePermission("orders:update")]
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
    [RequirePermission("orders:update")]
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
    [RequirePermission("orders:approve")]
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
    [RequirePermission("orders:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("delete", () => orders.DeleteAsync(id, ct));
        return NoContent();
    }
}
