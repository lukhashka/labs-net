using Dapper;
using Microsoft.Data.SqlClient;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

// Lab 08: the local copy of the Catalog reference (dbo.TicketTypes) is updated only by events of the Catalog.
// The Catalog stays the source of truth. OrderItems are never touched: their price is the price the client paid.
//
// Events may come out of order (a retry, a restart). Every update applies only a NEWER change:
// SourceUpdatedAt guards the fields of the ticket type, EventSourceUpdatedAt the fields of the event.
// The methods return false when the change is older than the one already applied.
public interface ICatalogReplicaRepository
{
    Task<bool> AddTicketTypeAsync(TicketType copy, DateTime changedAt, CancellationToken ct = default);
    Task<bool> ApplyPriceAsync(long ticketTypeId, string name, decimal price, DateTime changedAt, CancellationToken ct = default);
    Task<bool> TakeOffSaleAsync(long ticketTypeId, DateTime changedAt, CancellationToken ct = default);
    Task<int> ApplyEventTitleAsync(long eventId, string title, DateTime changedAt, CancellationToken ct = default);
    Task<int> ApplyEventScheduleAsync(long eventId, DateTime startsAt, DateTime changedAt, CancellationToken ct = default);
}

// Ids of processed events. The primary key makes a second insert of the same EventId fail.
public interface IProcessedEventRepository
{
    // false: the event was already processed (duplicate delivery)
    Task<bool> TryAddAsync(Guid eventId, CancellationToken ct = default);
}

public sealed class CatalogReplicaRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), ICatalogReplicaRepository
{
    public async Task<bool> AddTicketTypeAsync(TicketType copy, DateTime changedAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command("""
            IF NOT EXISTS (SELECT 1 FROM dbo.TicketTypes WHERE Id = @Id)
                INSERT dbo.TicketTypes (Id, EventId, EventTitle, EventStartsAt, Name, Price, Stock, SourceUpdatedAt, EventSourceUpdatedAt)
                VALUES (@Id, @EventId, @EventTitle, @EventStartsAt, @Name, @Price, @Stock, @ChangedAt, @ChangedAt)
            """, new { copy.Id, copy.EventId, copy.EventTitle, copy.EventStartsAt, copy.Name, copy.Price, copy.Stock, ChangedAt = changedAt }, ct)) > 0;
    }

    public async Task<bool> ApplyPriceAsync(long ticketTypeId, string name, decimal price, DateTime changedAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command("""
            UPDATE dbo.TicketTypes SET Name = @Name, Price = @Price, SourceUpdatedAt = @ChangedAt
            WHERE Id = @Id AND (SourceUpdatedAt IS NULL OR SourceUpdatedAt < @ChangedAt)
            """, new { Id = ticketTypeId, Name = name, Price = price, ChangedAt = changedAt }, ct)) > 0;
    }

    public async Task<bool> TakeOffSaleAsync(long ticketTypeId, DateTime changedAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command("""
            UPDATE dbo.TicketTypes SET Stock = 0, SourceUpdatedAt = @ChangedAt
            WHERE Id = @Id AND (SourceUpdatedAt IS NULL OR SourceUpdatedAt < @ChangedAt)
            """, new { Id = ticketTypeId, ChangedAt = changedAt }, ct)) > 0;
    }

    public async Task<int> ApplyEventTitleAsync(long eventId, string title, DateTime changedAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command("""
            UPDATE dbo.TicketTypes SET EventTitle = @Title, EventSourceUpdatedAt = @ChangedAt
            WHERE EventId = @EventId AND (EventSourceUpdatedAt IS NULL OR EventSourceUpdatedAt < @ChangedAt)
            """, new { EventId = eventId, Title = title, ChangedAt = changedAt }, ct));
    }

    public async Task<int> ApplyEventScheduleAsync(long eventId, DateTime startsAt, DateTime changedAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Command("""
            UPDATE dbo.TicketTypes SET EventStartsAt = @StartsAt, EventSourceUpdatedAt = @ChangedAt
            WHERE EventId = @EventId AND (EventSourceUpdatedAt IS NULL OR EventSourceUpdatedAt < @ChangedAt)
            """, new { EventId = eventId, StartsAt = startsAt, ChangedAt = changedAt }, ct));
    }
}

public sealed class ProcessedEventRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), IProcessedEventRepository
{
    public async Task<bool> TryAddAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        try
        {
            await Connection.ExecuteAsync(Command(
                "INSERT dbo.ProcessedEvents (EventId) VALUES (@EventId)", new { EventId = eventId }, ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)    // primary key violation: already processed
        {
            return false;
        }
    }
}
