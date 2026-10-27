namespace Reviews.Infrastructure.Persistence;

// Bound from the "MongoDb" section of appsettings.{Environment}.json
public class MongoDbSettings
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "";
}
