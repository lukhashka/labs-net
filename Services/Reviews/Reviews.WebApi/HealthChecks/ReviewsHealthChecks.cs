using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace Reviews.WebApi.HealthChecks;

public static class ReviewsHealthChecks
{
    // Readiness of the Reviews service: MongoDB, checked with a ping through the same IMongoClient the service uses
    public static IServiceCollection AddReviewsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddMongoDb(
                sp => sp.GetRequiredService<IMongoClient>(),
                name: "reviews-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "mongodb", "ready"],
                timeout: TimeSpan.FromSeconds(5));

        // Standard gRPC Health Checking Protocol for the Aggregator
        services.AddGrpcHealthChecks(o => o.Services.Map("", r => r.Tags.Contains("ready")));

        return services;
    }
}
