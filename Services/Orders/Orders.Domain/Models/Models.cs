namespace Orders.Domain.Models;

// Models repeat the tables of OrdersDb (Lab 01). Status names are the same as in the CHECK constraint.
public static class OrderStatus
{
    public const string Submitted = "Submitted";
    public const string AwaitingValidation = "AwaitingValidation";
    public const string StockConfirmed = "StockConfirmed";
    public const string Paid = "Paid";
    public const string Shipped = "Shipped";
    public const string Cancelled = "Cancelled";
}

public class Customer
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class Order
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = OrderStatus.Submitted;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderDetails? Details { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

// 1:1 with Order
public class OrderDetails
{
    public long OrderId { get; set; }
    public string AttendeeName { get; set; } = "";
    public string AttendeeEmail { get; set; } = "";
    public string? Comment { get; set; }
}

public class OrderItem
{
    public long OrderId { get; set; }
    public long TicketTypeId { get; set; }
    public long EventId { get; set; }                       // from TicketTypes, not stored in OrderItems
    public string EventTitle { get; set; } = "";            // snapshot at order time
    public string TicketTypeName { get; set; } = "";        // snapshot at order time
    public decimal UnitPrice { get; set; }                  // snapshot at order time
    public int Units { get; set; }
}

// Local copy of the Catalog reference data, Stock is owned by this service
public class TicketType
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public DateTime EventStartsAt { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

// Row of the view v_OrderSummary
public class OrderSummary
{
    public long Id { get; set; }
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public long CustomerId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public int TicketsCount { get; set; }
}
