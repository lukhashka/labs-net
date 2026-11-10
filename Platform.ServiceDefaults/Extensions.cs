using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

// Cross-cutting settings shared by every service: Serilog, OpenTelemetry, health checks, service discovery, CorrelationId
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    // Meters of all services: business metrics (Lab 07), cache metrics (Lab 06) and resilience events of Polly
    private static readonly string[] Meters =
        ["Orders.Api", "Catalog.Api", "Reviews.WebApi", "Catalog.Cache", "Aggregator.Cache", "Polly"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureSerilog();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // One Standard Resilience Handler for every HttpClient and gRPC client: rate limiter, total timeout,
            // retry, circuit breaker, attempt timeout. A client with other needs removes it first
            // (RemoveAllResilienceHandlers) and adds its own, handlers are never stacked (Platform.Aggregator).
            http.AddStandardResilienceHandler(o =>
            {
                o.Retry.UseJitter = true;                           // clients do not retry at the same moment
                o.Retry.DisableForUnsafeHttpMethods();              // POST/PUT/DELETE are not repeated: no double orders
            });
            http.AddServiceDiscovery();                             // http://orders-api -> real address from Aspire
            http.AddHttpMessageHandler<CorrelationIdHandler>();     // the same X-Correlation-Id in outgoing calls
        });

        return builder;
    }

    private static void ConfigureSerilog<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Providers are added explicitly: only OpenTelemetry remains behind Serilog, otherwise console lines are doubled
        builder.Logging.ClearProviders();

        builder.Services.AddSerilog((_, cfg) => cfg
            .MinimumLevel.Is(builder.Environment.IsDevelopment() ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", builder.Environment.ApplicationName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(new CompactJsonFormatter()),
            writeToProviders: true);    // -> OpenTelemetry provider -> tab "Structured logs" of the Dashboard
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(Meters))
            .WithTracing(tracing =>
            {
                // 100 % in Development, a configurable share in Production; a child follows the decision of its parent
                var ratio = builder.Environment.IsDevelopment()
                    ? 1.0
                    : builder.Configuration.GetValue("OpenTelemetry:Sampling:Ratio", 0.1);

                tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio)))
                    .AddSource(builder.Environment.ApplicationName)                     // own spans of business operations
                    .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")      // MongoDB
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        o.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                    })
                    .AddHttpClientInstrumentation(o => o.RecordException = true)
                    .AddSqlClientInstrumentation();                                     // both Dapper and EF Core use SqlClient
            });

        // The address is set by Aspire. The OTLP exporter works in batches and does not block requests.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Liveness: the process answers. No external dependency here, otherwise a database failure would restart the pod.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    // The endpoints are mapped in every environment: Kubernetes probes (Lab 10) and the Aspire Dashboard need them.
    // Answers contain only names, statuses and durations of checks, no connection strings and no exception texts.
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // all checks: the state of the resource in the Aspire Dashboard (WithHttpHealthCheck in AppHost)
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteJson });

        // liveness: only the process itself, must answer in < 100 ms
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        app.MapHealthChecks(HealthEndpointPath + "/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });

        // readiness: every dependency needed to serve traffic (tag "ready"), details of each check in JSON.
        // Degraded (cache) still gives 200: the service can take traffic, Unhealthy gives 503.
        app.MapHealthChecks(HealthEndpointPath + "/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = HealthResponseWriter.WriteJson
        });

        return app;
    }
}
