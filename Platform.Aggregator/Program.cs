using System.Text.Json.Serialization;
using Catalog.Grpc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orders.Grpc;
using Platform.Aggregator.Clients;
using Platform.Aggregator.HealthChecks;
using Platform.Aggregator.Resilience;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;
using Reviews.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var redisConnection = builder.Configuration.GetConnectionString("redis");
if (!string.IsNullOrEmpty(redisConnection))
{
    builder.AddRedisDistributedCache("redis",
        configureSettings: s => s.DisableHealthChecks = true,     // own check below: Degraded instead of Unhealthy
        configureOptions: o =>
        {
            o.ConnectTimeout = 1000;
            o.SyncTimeout = 1000;
            o.AsyncTimeout = 1000;
        });
    // The aggregate cache is optional: without Redis the Aggregator calls the services directly
    builder.Services.AddHealthChecks().AddRedis(redisConnection, name: "redis-cache",
        failureStatus: HealthStatus.Degraded, tags: ["cache", "redis", "ready"], timeout: TimeSpan.FromSeconds(3));
}
else
    builder.Services.AddDistributedMemoryCache();

// Every client has the policy of its service: critical orders-api and catalog-api, non-critical reviews-api
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddResilientGrpcClient<OrdersService.OrdersServiceClient>(DownstreamPolicy.Orders, isDevelopment);
builder.Services.AddResilientGrpcClient<CatalogService.CatalogServiceClient>(DownstreamPolicy.Catalog, isDevelopment);
builder.Services.AddResilientGrpcClient<ReviewsService.ReviewsServiceClient>(DownstreamPolicy.Reviews, isDevelopment);

// Readiness of the Aggregator: the services it depends on, asked over the gRPC Health Checking Protocol.
// Without reviews-api the Aggregator still works (partial answers), so it becomes Degraded, not Unhealthy.
builder.Services.AddHealthChecks()
    .AddGrpcDependency(builder.Services, "orders-api", HealthStatus.Unhealthy, isDevelopment)
    .AddGrpcDependency(builder.Services, "catalog-api", HealthStatus.Unhealthy, isDevelopment)
    .AddGrpcDependency(builder.Services, "reviews-api", HealthStatus.Degraded, isDevelopment);

builder.Services.AddScoped<OrdersClient>();
builder.Services.AddScoped<CatalogClient>();
builder.Services.AddScoped<ReviewsClient>();
builder.Services.AddSingleton<AggregateCache>();
builder.Services.AddScoped<AggregationService>();

// ConfigureHttpJsonOptions works only for minimal APIs, controllers need AddJsonOptions
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapDefaultEndpoints();
app.MapControllers();

app.Run();

