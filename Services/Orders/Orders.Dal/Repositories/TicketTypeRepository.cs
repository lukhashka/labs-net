using Dapper;
using Microsoft.Data.SqlClient;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

public sealed class TicketTypeRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<TicketType>(connection, currentTransaction, "dbo.TicketTypes"), ITicketTypeRepository
{
    protected override string InsertSql => """
        INSERT dbo.TicketTypes (Id, EventId, EventTitle, EventStartsAt, Name, Price, Stock)
        OUTPUT INSERTED.Id
        VALUES (@Id, @EventId, @EventTitle, @EventStartsAt, @Name, @Price, @Stock)
        """;

    public async Task<IReadOnlyList<TicketType>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // Dapper expands @Ids into a list of parameters: IN (@Ids1, @Ids2, ...)
        var rows = await Connection.QueryAsync<TicketType>(Command(
            "SELECT * FROM dbo.TicketTypes WHERE Id IN @Ids", new { Ids = ids.ToArray() }, ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TicketType>> GetByEventAsync(long eventId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<TicketType>(Command(
            "SELECT * FROM dbo.TicketTypes WHERE EventId = @EventId ORDER BY Id", new { EventId = eventId }, ct));
        return rows.AsList();
    }

    public async Task<bool> UpdateAsync(TicketType ticketType, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command("""
            UPDATE dbo.TicketTypes
            SET Name = @Name, Price = @Price, SourceUpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """, ticketType, ct)) > 0;
    }

    // One atomic UPDATE: the condition and the write-off cannot be split by a parallel request
    public async Task DecreaseStockAsync(long ticketTypeId, int quantity, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var affected = await Connection.ExecuteAsync(Command("""
            UPDATE dbo.TicketTypes SET Stock = Stock - @Quantity
            WHERE Id = @Id AND Stock >= @Quantity
            """, new { Id = ticketTypeId, Quantity = quantity }, ct));

        if (affected == 0)
            throw new BusinessConflictException($"Not enough tickets of type {ticketTypeId}");
    }
}
