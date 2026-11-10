using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

// Same CRUD for all Dapper repositories. Methods return models, never SqlDataReader or DataTable.
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

// Implemented with plain ADO.NET, so it deliberately does not inherit the generic contract
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(Customer customer, CancellationToken ct = default);
    Task<bool> UpdateAsync(Customer customer, CancellationToken ct = default);   // false = RowVersion mismatch
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);             // soft delete
    Task<long?> GetIdByExternalUserAsync(string externalUserId, CancellationToken ct = default);   // Lab 09
    Task LinkExternalUserAsync(long id, string externalUserId, CancellationToken ct = default);    // Lab 09
}

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetWithItemsAsync(long orderId, CancellationToken ct = default);
    Task<(IReadOnlyList<OrderSummary> Items, int Total)> GetPageAsync(
        long? customerId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task AddDetailsAsync(OrderDetails details, CancellationToken ct = default);
    Task<bool> UpdateDetailsAsync(OrderDetails details, CancellationToken ct = default);           // Lab 09
    Task AddItemAsync(OrderItem item, CancellationToken ct = default);
    Task ConfirmAsync(long orderId, string? changedBy, CancellationToken ct = default);                  // usp_ConfirmOrder
    Task ChangeStatusAsync(long orderId, string newStatus, string? changedBy, CancellationToken ct = default); // usp_ChangeOrderStatus
    Task CancelAsync(long orderId, string? changedBy, CancellationToken ct = default);                   // usp_CancelOrder
    Task RegisterPaymentAsync(long orderId, decimal amount, string method, string externalRef, CancellationToken ct = default); // usp_RegisterPayment
    Task IssueTicketsAsync(long orderId, CancellationToken ct = default);                                // usp_IssueTickets
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken ct = default);           // gauge of Lab 07
}

public interface ITicketTypeRepository : IGenericRepository<TicketType>
{
    Task<IReadOnlyList<TicketType>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken ct = default);
    Task<IReadOnlyList<TicketType>> GetByEventAsync(long eventId, CancellationToken ct = default);
    Task<bool> UpdateAsync(TicketType ticketType, CancellationToken ct = default);
    Task DecreaseStockAsync(long ticketTypeId, int quantity, CancellationToken ct = default);
}
