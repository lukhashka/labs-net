using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

// Creates indexes on startup. CreateMany is idempotent: existing indexes are not recreated.
public class IndexCreationService(MongoDbContext context, ILogger<IndexCreationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var keys = Builders<Review>.IndexKeys;
            await context.Reviews.Indexes.CreateManyAsync(new[]
            {
                // latest reviews of an event
                new CreateIndexModel<Review>(
                    keys.Ascending(r => r.EventId).Descending(r => r.CreatedAt),
                    new CreateIndexOptions { Name = "eventId_createdAt" }),
                // full-text search by review text
                new CreateIndexModel<Review>(
                    keys.Text(r => r.Text),
                    new CreateIndexOptions { Name = "text_search" }),
                // one review from an author per event
                new CreateIndexModel<Review>(
                    keys.Ascending(r => r.EventId).Ascending("author.userId"),
                    new CreateIndexOptions { Name = "eventId_author_unique", Unique = true })
            }, ct);

            var dKeys = Builders<Discussion>.IndexKeys;
            await context.Discussions.Indexes.CreateOneAsync(
                new CreateIndexModel<Discussion>(
                    dKeys.Ascending(d => d.EventId).Descending(d => d.CreatedAt),
                    new CreateIndexOptions { Name = "eventId_createdAt" }),
                cancellationToken: ct);

            logger.LogInformation("MongoDB indexes are ready");
        }
        catch (Exception ex)
        {
            // Do not crash the API if Mongo is down: requests will get 503
            logger.LogError(ex, "Could not create MongoDB indexes");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
