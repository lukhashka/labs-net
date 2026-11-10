using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orders.Api.HealthChecks;

public static class OrdersHealthChecks
{
    // Readiness of the Orders service: its own database. The check is light (SELECT 1) because it runs regularly.
    public static IServiceCollection AddOrdersHealthChecks(this IServiceCollection services, string connectionString)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: connectionString,
                healthQuery: "SELECT 1;",
                name: "orders-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "sql", "ready"],
                timeout: TimeSpan.FromSeconds(5));

        // Standard gRPC Health Checking Protocol: the Aggregator asks it. It answers from the checks tagged "ready".
        services.AddGrpcHealthChecks(o => o.Services.Map("", r => r.Tags.Contains("ready")));

        return services;
    }
}
