using System.Data;
using Microsoft.Data.SqlClient;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

// Plain ADO.NET without third-party libraries: SqlCommand, parameters, SqlDataReader and manual mapping
public sealed class CustomerRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), ICustomerRepository
{
    private const string Columns = "Id, Email, FullName, Phone, RowVersion";

    public async Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(
            $"SELECT {Columns} FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0", Connection, Transaction);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(
            $"SELECT {Columns} FROM dbo.Customers WHERE IsDeleted = 0 ORDER BY Id", Connection, Transaction);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var customers = new List<Customer>();
        while (await reader.ReadAsync(ct))
            customers.Add(Map(reader));
        return customers;
    }

    public async Task<long> AddAsync(Customer customer, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(
            "INSERT dbo.Customers (Email, FullName, Phone) OUTPUT INSERTED.Id VALUES (@Email, @FullName, @Phone)",
            Connection, Transaction);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = (object?)customer.Phone ?? DBNull.Value;

        try
        {
            return (long)(await command.ExecuteScalarAsync(ct))!;
        }
        catch (SqlException ex) when (Translate(ex) is not SqlException)
        {
            throw Translate(ex);
        }
    }

    // Optimistic concurrency: the row is updated only if nobody changed it since it was read
    public async Task<bool> UpdateAsync(Customer customer, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE dbo.Customers
            SET FullName = @FullName, Phone = @Phone, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND RowVersion = @RowVersion AND IsDeleted = 0
            """, Connection, Transaction);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = customer.Id;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = (object?)customer.Phone ?? DBNull.Value;
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = customer.RowVersion;

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(
            "UPDATE dbo.Customers SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND IsDeleted = 0",
            Connection, Transaction);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private static Customer Map(SqlDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Email = reader.GetString(1),
        FullName = reader.GetString(2),
        Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
        RowVersion = (byte[])reader[4]
    };
}
