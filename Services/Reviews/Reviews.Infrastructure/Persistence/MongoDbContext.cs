using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

// Single place that knows the database and collection names.
// Registered as singleton: MongoClient is thread-safe and keeps the connection pool.
public class MongoDbContext(IMongoClient client, IOptions<MongoDbSettings> options)
{
    public IMongoDatabase Database { get; } = client.GetDatabase(options.Value.DatabaseName);

    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>("reviews");

    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>("discussions");

    // One MongoClient for the whole service, registered in DI: the context and the health check (Lab 07) share it.
    // The driver reports every command to OpenTelemetry: DiagnosticsActivityEventSubscriber creates the spans
    // of the source "MongoDB.Driver.Core.Extensions.DiagnosticSources" that ServiceDefaults listens to.
    public static IMongoClient CreateClient(MongoDbSettings settings)
    {
        var clientSettings = MongoClientSettings.FromConnectionString(settings.ConnectionString);
        clientSettings.ClusterConfigurator = cb => cb.Subscribe(
            new DiagnosticsActivityEventSubscriber(new InstrumentationOptions { CaptureCommandText = true }));
        return new MongoClient(clientSettings);
    }
}
