using System.Text.Json.Serialization;
using Microsoft.Extensions.Http.Resilience;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// By default one call may wait 10 s per attempt. A card must answer quickly, a warning is better than waiting.
builder.Services.ConfigureAll<HttpStandardResilienceOptions>(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.Retry.MaxRetryAttempts = 1;
    o.Retry.Delay = TimeSpan.FromMilliseconds(200);
});

// "https+http": HTTPS if the service has it, otherwise HTTP
builder.Services.AddHttpClient<OrdersClient>(c => c.BaseAddress = new("https+http://orders-api"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new("https+http://catalog-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"));
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
