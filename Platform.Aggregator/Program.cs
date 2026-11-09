using System.Net.Security;
using System.Text.Json.Serialization;
using Catalog.Grpc;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Http.Resilience;
using Orders.Grpc;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;
using Reviews.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis", configureOptions: o =>
    {
        o.ConnectTimeout = 1000;
        o.SyncTimeout = 1000;
        o.AsyncTimeout = 1000;
    });
else
    builder.Services.AddDistributedMemoryCache();

// By default one call may wait 10 s per attempt. A card must answer quickly, a warning is better than waiting.
builder.Services.ConfigureAll<HttpStandardResilienceOptions>(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.Retry.MaxRetryAttempts = 1;
    o.Retry.Delay = TimeSpan.FromMilliseconds(200);
});

// gRPC clients are created by the HttpClient factory, so service discovery, resilience and CorrelationIdHandler
// from ServiceDefaults work for them too. The name of the service is resolved like for HttpClient in Lab 05.
AddGrpcClient<OrdersService.OrdersServiceClient>("https://orders-api");
AddGrpcClient<CatalogService.CatalogServiceClient>("https://catalog-api");
AddGrpcClient<ReviewsService.ReviewsServiceClient>("https://reviews-api");

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

void AddGrpcClient<TClient>(string address) where TClient : class
{
    var client = builder.Services.AddGrpcClient<TClient>(o => o.Address = new Uri(address));

    client.ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };

        // Development only: accept the ASP.NET Core development certificate even if the machine does not trust it.
        // Every other certificate and every other environment go through the normal validation.
        if (builder.Environment.IsDevelopment())
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None
                || certificate is System.Security.Cryptography.X509Certificates.X509Certificate2 c
                   && c.Extensions.Any(e => e.Oid?.Value == "1.3.6.1.4.1.311.84.1.1");   // OID of the ASP.NET Core HTTPS development certificate

        return handler;
    });
}
