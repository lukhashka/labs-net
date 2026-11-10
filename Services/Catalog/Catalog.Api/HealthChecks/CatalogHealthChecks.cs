using Catalog.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.Api.HealthChecks;

public static class CatalogHealthChecks
{
    public static IServiceCollection AddCatalogHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks()
            // The database is required: without it the service cannot answer, so the failure is Unhealthy
            .AddSqlServer(
                connectionString: configuration.GetConnectionString("CatalogDb")!,
                healthQuery: "SELECT 1;",
                name: "catalog-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "sql", "ready"],
                timeout: TimeSpan.FromSeconds(5))
            // Hit ratios of L1 and L2 for diagnostics, always Healthy
            .AddCheck<CacheHealthCheck>("cache-statistics", tags: ["cache"]);

        // Redis is optional: the service reads from the database without it (Lab 06), so only Degraded
        var redis = configuration.GetConnectionString("redis");
        if (!string.IsNullOrEmpty(redis))
            checks.AddRedis(
                redisConnectionString: redis,
                name: "redis-cache",
                failureStatus: HealthStatus.Degraded,
                tags: ["cache", "redis", "ready"],
                timeout: TimeSpan.FromSeconds(3));

        // Standard gRPC Health Checking Protocol for the Aggregator: can the service answer gRPC calls?
        // The optional cache is left out: a hanging Redis (3 s timeout) must not make the Aggregator,
        // which waits 2 s, think that the whole Catalog is down. Redis is visible in /health/ready as Degraded.
        services.AddGrpcHealthChecks(o => o.Services.Map("",
            r => r.Tags.Contains("ready") && !r.Tags.Contains("cache")));

        return services;
    }
}
