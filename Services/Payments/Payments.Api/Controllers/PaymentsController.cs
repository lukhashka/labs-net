using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Platform.ServiceDefaults.Authentication;

namespace Payments.Api.Controllers;

public record PaymentDto(Guid Id, long OrderId, decimal Amount, string Status, DateTime CreatedAt, string ProcessedBy);
public record ProcessPaymentRequest([Range(1, long.MaxValue)] long OrderId, [Range(0.01, 1_000_000)] decimal Amount);
public record RefundRequest([Required] Guid PaymentId);

// Stubs: the point is the authorization. Payments are the most sensitive resource, so the permissions
// payments:* are given only to admin (Table 10); manager, user and guest get 403 on every endpoint.
[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public class PaymentsController(ILogger<PaymentsController> logger) : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, PaymentDto> Payments = new();

    [HttpGet]
    [RequirePermission("payments:read")]
    public IEnumerable<PaymentDto> GetAll() => Payments.Values.OrderByDescending(p => p.CreatedAt);

    [HttpPost("process")]
    [RequirePermission("payments:process")]
    public ActionResult<PaymentDto> Process(ProcessPaymentRequest request)
    {
        var payment = new PaymentDto(Guid.NewGuid(), request.OrderId, request.Amount, "Processed", DateTime.UtcNow, UserName());
        Payments[payment.Id] = payment;
        logger.LogInformation("Payment {PaymentId} of order {OrderId} processed by {User}", payment.Id, payment.OrderId, payment.ProcessedBy);
        return Created($"/api/payments/{payment.Id}", payment);
    }

    [HttpPost("refund")]
    [RequirePermission("payments:refund")]
    public ActionResult<PaymentDto> Refund(RefundRequest request)
    {
        if (!Payments.TryGetValue(request.PaymentId, out var payment))
            return Problem(statusCode: 404, title: $"Payment {request.PaymentId} not found");

        var refunded = payment with { Status = "Refunded" };
        Payments[payment.Id] = refunded;
        return Ok(refunded);
    }

    private string UserName() =>
        User.FindFirst("preferred_username")?.Value ?? User.FindFirst("unique_name")?.Value ?? User.FindFirst("sub")?.Value ?? "unknown";
}
