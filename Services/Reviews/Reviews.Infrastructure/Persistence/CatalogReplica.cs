using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

// Collections of Lab 08:
//   processedEvents {_id: EventId}                 - the _id is unique, so an EventId can be stored only once
//   catalogSync     {_id: eventId, titleChangedAt} - the time of the last applied rename (protection from old events)
public class CatalogReplica(MongoDbContext context) : ICatalogReplica
{
    private IMongoCollection<BsonDocument> Processed => context.Database.GetCollection<BsonDocument>("processedEvents");
    private IMongoCollection<BsonDocument> Versions => context.Database.GetCollection<BsonDocument>("catalogSync");

    public Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct)
        => Processed.Find(Builders<BsonDocument>.Filter.Eq("_id", eventId.ToString())).AnyAsync(ct);

    public async Task<DateTime?> GetTitleChangedAtAsync(long catalogEventId, CancellationToken ct)
    {
        var doc = await Versions.Find(Builders<BsonDocument>.Filter.Eq("_id", catalogEventId)).FirstOrDefaultAsync(ct);
        return doc is not null && doc.TryGetValue("titleChangedAt", out var value) ? value.ToUniversalTime() : null;
    }

    public async Task<(long Reviews, long Discussions)> SetEventTitleAsync(long catalogEventId, string title, CancellationToken ct)
    {
        // one UpdateMany per collection, not a loop over documents
        var reviews = await context.Reviews.UpdateManyAsync(
            Builders<Review>.Filter.Eq(r => r.EventId, catalogEventId),
            Builders<Review>.Update.Set(r => r.EventTitle, title),
            cancellationToken: ct);
        var discussions = await context.Discussions.UpdateManyAsync(
            Builders<Discussion>.Filter.Eq(d => d.EventId, catalogEventId),
            Builders<Discussion>.Update.Set(d => d.EventTitle, title),
            cancellationToken: ct);
        return (reviews.ModifiedCount, discussions.ModifiedCount);
    }

    public Task SaveTitleChangedAtAsync(long catalogEventId, DateTime changedAt, CancellationToken ct)
        => Versions.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", catalogEventId),
            Builders<BsonDocument>.Update.Set("titleChangedAt", changedAt),
            new UpdateOptions { IsUpsert = true }, ct);

    public async Task MarkProcessedAsync(Guid eventId, CancellationToken ct)
    {
        try
        {
            await Processed.InsertOneAsync(new BsonDocument
            {
                ["_id"] = eventId.ToString(),
                ["processedAt"] = DateTime.UtcNow
            }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // already stored by a parallel delivery of the same event
        }
    }
}
