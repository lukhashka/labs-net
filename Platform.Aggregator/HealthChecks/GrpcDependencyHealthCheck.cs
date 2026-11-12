using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.Aggregator.Resilience;

namespace Platform.Aggregator.HealthChecks;

// Asks a downstream service over the standard gRPC Health Checking Protocol.
// The call is read-only and has a short deadline, so a hanging service is detected in 2 s.
public class GrpcDependencyHealthCheck(GrpcClientFactory factory, ILogger<GrpcDependencyHealthCheck> logger, string clientName)
    : IHealthCheck
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var service = context.Registration.Name;
        try
        {
            var client = factory.CreateClient<Health.HealthClient>(clientName);
            var reply = await client.CheckAsync(new HealthCheckRequest(),
                deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);

            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{service} is serving")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{service} status: {reply.Status}");
        }
        catch (RpcException ex)
        {
            logger.LogWarning("Health check of {Service} failed: {StatusCode}", service, ex.StatusCode);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{service} is unavailable ({ex.StatusCode})");
        }
    }
}

public static class DependencyHealthChecks
{
    // A separate named Health client per service, without the resilience handler:
    // a health check must show the current state, not retry until the deadline
    public static IHealthChecksBuilder AddGrpcDependency(this IHealthChecksBuilder checks, IServiceCollection services,
        string service, HealthStatus failureStatus, bool isDevelopment)
    {
        var clientName = service + "-health";
#pragma warning disable EXTEXP0001   // RemoveAllResilienceHandlers is marked experimental
        services.AddGrpcClient<Health.HealthClient>(clientName,
                (sp, o) => o.Address = ResilientGrpcClients.GrpcAddress(sp.GetRequiredService<IConfiguration>(), service))
            .ConfigurePrimaryHttpMessageHandler(() => ResilientGrpcClients.CreateHandler(isDevelopment))
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return checks.AddTypeActivatedCheck<GrpcDependencyHealthCheck>(
            service, failureStatus, ["grpc", "downstream", "ready"], clientName);
    }
}
