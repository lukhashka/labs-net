using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Orders.Domain.Exceptions;

namespace Orders.Dal.Repositories;

// All repositories of one business operation get the same connection and access to the current transaction
public abstract class RepositoryBase(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
{
    protected SqlConnection Connection { get; } = connection;

    protected SqlTransaction? Transaction => currentTransaction();

    protected async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
            await Connection.OpenAsync(ct);
    }

    // Every call carries the UoW transaction and the cancellation token
    protected CommandDefinition Command(string sql, object? parameters, CancellationToken ct,
        CommandType commandType = CommandType.Text)
        => new(sql, parameters, Transaction, commandType: commandType, cancellationToken: ct);

    // SQL error numbers become domain exceptions here, raw SQL messages never reach the API
    protected static Exception Translate(SqlException ex) => ex.Number switch
    {
        50001 => new NotFoundException(ex.Message),
        50002 => new BusinessConflictException(ex.Message),
        2601 or 2627 => new BusinessConflictException("Record with the same key already exists"),
        _ => ex
    };
}

// CRUD is written once here, concrete repositories add only specific queries.
// tableName is a constant from code, not user input, so putting it into SQL text is safe.
public abstract class BaseDapperRepository<T>(
    SqlConnection connection, Func<SqlTransaction?> currentTransaction, string tableName)
    : RepositoryBase(connection, currentTransaction), IGenericRepository<T> where T : class
{
    protected string TableName { get; } = tableName;

    // INSERT ... OUTPUT INSERTED.Id VALUES ... with parameters named like properties of T
    protected abstract string InsertSql { get; }

    public virtual async Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.QueryFirstOrDefaultAsync<T>(Command(
            $"SELECT * FROM {TableName} WHERE Id = @Id", new { Id = id }, ct));
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<T>(Command($"SELECT * FROM {TableName} ORDER BY Id", null, ct));
        return rows.AsList();
    }

    public virtual async Task<long> AddAsync(T entity, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        try
        {
            return await Connection.ExecuteScalarAsync<long>(Command(InsertSql, entity, ct));
        }
        catch (SqlException ex) when (Translate(ex) is not SqlException)
        {
            throw Translate(ex);
        }
    }

    public virtual async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command($"DELETE FROM {TableName} WHERE Id = @Id", new { Id = id }, ct)) > 0;
    }
}
