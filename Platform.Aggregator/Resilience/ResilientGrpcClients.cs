using System.Net.Security;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.Aggregator.Resilience;

public static class ResilientGrpcClients
{
    // Typed gRPC client with the policy of its service. The client factory is the HttpClient factory,
    // so service discovery and CorrelationIdHandler from ServiceDefaults still work.
    public static IHttpClientBuilder AddResilientGrpcClient<TClient>(
        this IServiceCollection services, DownstreamPolicy policy, bool isDevelopment) where TClient : class
    {
        policy.EnsureConsistent();

        var client = services
            .AddGrpcClient<TClient>(o => o.Address = new Uri($"https://{policy.Service}"))
            .ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        // All methods of these clients are reads, so repeating them is safe
                        Names = { MethodName.Default },
                        RetryPolicy = new RetryPolicy
                        {
                            MaxAttempts = policy.GrpcMaxAttempts,
                            InitialBackoff = policy.GrpcInitialBackoff,
                            MaxBackoff = TimeSpan.FromSeconds(2),
                            BackoffMultiplier = 2,      // exponential; gRPC picks a random delay up to it (jitter)
                            RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                        }
                    }
                }
            })
            .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(isDevelopment));

        // The default handler from ServiceDefaults is removed first: handlers must not be stacked
#pragma warning disable EXTEXP0001   // RemoveAllResilienceHandlers is marked experimental, the lab requires exactly it
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        client.AddStandardResilienceHandler().Configure((o, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Platform.Aggregator.Resilience");

            // gRPC always uses POST. The read methods are idempotent, so network retries stay enabled here,
            // unlike the default for REST clients in ServiceDefaults (DisableForUnsafeHttpMethods).
            o.Retry.MaxRetryAttempts = policy.HttpRetries;
            o.Retry.Delay = policy.HttpRetryDelay;
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;

            o.AttemptTimeout.Timeout = policy.AttemptTimeout;
            // one gRPC attempt with its network retries; the gRPC deadline (CallTimeout) limits the whole call
            o.TotalRequestTimeout.Timeout = policy.CallTimeout;

            o.CircuitBreaker.FailureRatio = policy.FailureRatio;
            o.CircuitBreaker.MinimumThroughput = policy.MinimumThroughput;   // the test stand has little traffic
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);     // at least 2 x attempt timeout
            o.CircuitBreaker.BreakDuration = policy.BreakDuration;
            o.CircuitBreaker.OnOpened = args =>
            {
                logger.LogError("Circuit to {Service} opened for {BreakSeconds} s, calls are rejected without waiting",
                    policy.Service, args.BreakDuration.TotalSeconds);
                return ValueTask.CompletedTask;
            };
            o.CircuitBreaker.OnHalfOpened = _ =>
            {
                logger.LogInformation("Circuit to {Service} is half-open, a trial call goes through", policy.Service);
                return ValueTask.CompletedTask;
            };
            o.CircuitBreaker.OnClosed = _ =>
            {
                logger.LogInformation("Circuit to {Service} closed, calls go through again", policy.Service);
                return ValueTask.CompletedTask;
            };
        });

        return client;
    }

    public static SocketsHttpHandler CreateHandler(bool isDevelopment)
    {
        var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };

        // Development only: accept the ASP.NET Core development certificate even if the machine does not trust it.
        // Every other certificate and every other environment go through the normal validation.
        if (isDevelopment)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None
                || certificate is System.Security.Cryptography.X509Certificates.X509Certificate2 c
                   && c.Extensions.Any(e => e.Oid?.Value == "1.3.6.1.4.1.311.84.1.1");   // OID of the ASP.NET Core HTTPS development certificate

        return handler;
    }
}
