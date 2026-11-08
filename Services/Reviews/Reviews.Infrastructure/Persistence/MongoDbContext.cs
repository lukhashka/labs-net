using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

// Single place that knows the database and collection names.
// Registered as singleton: MongoClient is thread-safe and keeps the connection pool.
public class MongoDbContext
{
    public MongoDbContext(IOptions<MongoDbSettings> options)
    {
        var settings = options.Value;
        // the driver reports commands to OpenTelemetry
        var clientSettings = MongoClientSettings.FromConnectionString(settings.ConnectionString);
        clientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber());
        var client = new MongoClient(clientSettings);
        Database = client.GetDatabase(settings.DatabaseName);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>("reviews");

    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>("discussions");
}
