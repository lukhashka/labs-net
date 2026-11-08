using System.Data;
using Microsoft.Data.SqlClient;
using Orders.Dal.Repositories;

namespace Orders.Dal;

public interface IUnitOfWork : IAsyncDisposable
{
    ICustomerRepository Customers { get; }
    IOrderRepository Orders { get; }
    ITicketTypeRepository TicketTypes { get; }

    Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

// Scoped: one instance and one connection per HTTP request.
// Isolation level: READ COMMITTED. It does not hold shared locks until commit, so readers and writers rarely block
// each other and deadlocks are unlikely. What READ COMMITTED does not protect is covered explicitly:
// procedures take UPDLOCK on the order row, the write-off is one atomic UPDATE with a condition on Stock.
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    public UnitOfWork(string connectionString)
    {
        _connection = new SqlConnection(connectionString);

        // all repositories receive ONE connection and access to the CURRENT transaction
        Customers = new CustomerRepository(_connection, () => _transaction);
        Orders = new OrderRepository(_connection, () => _transaction);
        TicketTypes = new TicketTypeRepository(_connection, () => _transaction);
    }

    public ICustomerRepository Customers { get; }
    public IOrderRepository Orders { get; }
    public ITicketTypeRepository TicketTypes { get; }

    public async Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);
        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(level, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction!.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            return;

        // A procedure with XACT_ABORT ON has already rolled the transaction back on the server.
        // Such a SqlTransaction is "zombied": Connection is null and calling Rollback would throw.
        if (_transaction.Connection is not null)
            await _transaction.RollbackAsync(ct);

        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
