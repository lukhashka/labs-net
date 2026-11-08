using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

public sealed class OrderRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<Order>(connection, currentTransaction, "dbo.Orders"), IOrderRepository
{
    protected override string InsertSql => """
        INSERT dbo.Orders (CustomerId, Status, TotalAmount)
        OUTPUT INSERTED.Id
        VALUES (@CustomerId, @Status, @TotalAmount)
        """;

    // Multi-mapping: every row of the JOIN is Order + one item
    public async Task<Order?> GetWithItemsAsync(long orderId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        Order? result = null;
        await Connection.QueryAsync<Order, OrderItem?, Order>(
            Command("dbo.usp_GetOrderWithItems", new { OrderId = orderId }, ct, CommandType.StoredProcedure),
            (order, item) =>
            {
                result ??= order;
                if (item is not null)
                    result.Items.Add(item);
                return result;
            },
            splitOn: "OrderId");                     // OrderItem starts from this column

        if (result is not null)
            result.Details = await Connection.QueryFirstOrDefaultAsync<OrderDetails>(Command(
                "SELECT OrderId, AttendeeName, AttendeeEmail, Comment FROM dbo.OrderDetails WHERE OrderId = @OrderId",
                new { OrderId = orderId }, ct));

        return result;
    }

    public async Task<(IReadOnlyList<OrderSummary> Items, int Total)> GetPageAsync(
        long? customerId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(*) FROM dbo.v_OrderSummary
            WHERE (@CustomerId IS NULL OR CustomerId = @CustomerId) AND (@Status IS NULL OR Status = @Status);

            SELECT * FROM dbo.v_OrderSummary
            WHERE (@CustomerId IS NULL OR CustomerId = @CustomerId) AND (@Status IS NULL OR Status = @Status)
            ORDER BY OrderDate DESC, Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        await EnsureOpenAsync(ct);
        using var grid = await Connection.QueryMultipleAsync(Command(sql,
            new { CustomerId = customerId, Status = status, Offset = (page - 1) * pageSize, PageSize = pageSize }, ct));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<OrderSummary>()).AsList();
        return (items, total);
    }

    public async Task AddDetailsAsync(OrderDetails details, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await Connection.ExecuteAsync(Command("""
            INSERT dbo.OrderDetails (OrderId, AttendeeName, AttendeeEmail, Comment)
            VALUES (@OrderId, @AttendeeName, @AttendeeEmail, @Comment)
            """, details, ct));
    }

    public async Task AddItemAsync(OrderItem item, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await Connection.ExecuteAsync(Command("""
            INSERT dbo.OrderItems (OrderId, TicketTypeId, EventTitle, TicketTypeName, UnitPrice, Units)
            VALUES (@OrderId, @TicketTypeId, @EventTitle, @TicketTypeName, @UnitPrice, @Units)
            """, item, ct));
    }

    public override async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command(
            "UPDATE dbo.Orders SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND IsDeleted = 0",
            new { Id = id }, ct)) > 0;
    }

    public override async Task<Order?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.QueryFirstOrDefaultAsync<Order>(Command(
            "SELECT Id, CustomerId, Status, OrderDate, TotalAmount FROM dbo.Orders WHERE Id = @Id AND IsDeleted = 0",
            new { Id = id }, ct));
    }

    public Task ConfirmAsync(long orderId, string? changedBy, CancellationToken ct = default)
        => ExecuteProcedureAsync("dbo.usp_ConfirmOrder", new { OrderId = orderId, ChangedBy = changedBy }, ct);

    public Task ChangeStatusAsync(long orderId, string newStatus, string? changedBy, CancellationToken ct = default)
        => ExecuteProcedureAsync("dbo.usp_ChangeOrderStatus",
            new { OrderId = orderId, NewStatus = newStatus, ChangedBy = changedBy }, ct);

    public Task CancelAsync(long orderId, string? changedBy, CancellationToken ct = default)
        => ExecuteProcedureAsync("dbo.usp_CancelOrder", new { OrderId = orderId, ChangedBy = changedBy }, ct);

    public Task RegisterPaymentAsync(long orderId, decimal amount, string method, string externalRef, CancellationToken ct = default)
        => ExecuteProcedureAsync("dbo.usp_RegisterPayment",
            new { OrderId = orderId, Amount = amount, Method = method, ExternalRef = externalRef }, ct);

    public Task IssueTicketsAsync(long orderId, CancellationToken ct = default)
        => ExecuteProcedureAsync("dbo.usp_IssueTickets", new { OrderId = orderId }, ct);

    // THROW 50001/50002 from a procedure -> domain exception -> 404/409 in the API
    private async Task ExecuteProcedureAsync(string name, object parameters, CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        try
        {
            await Connection.ExecuteAsync(Command(name, parameters, ct, CommandType.StoredProcedure));
        }
        catch (SqlException ex) when (Translate(ex) is not SqlException)
        {
            throw Translate(ex);
        }
    }
}
