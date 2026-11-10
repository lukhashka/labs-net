using Platform.EventBus;
using Platform.ServiceDefaults;
using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using Reviews.WebApi.GrpcServices;
using Reviews.WebApi.HealthChecks;
using Reviews.WebApi.IntegrationEvents;
using Reviews.WebApi.Metrics;
using Reviews.WebApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();                    // logging, tracing, health checks, service discovery

// Aspire passes ConnectionStrings:ReviewsDb, the service of Lab 04 reads the section MongoDb
var reviewsDb = builder.Configuration.GetConnectionString("ReviewsDb");
if (!string.IsNullOrEmpty(reviewsDb))
    builder.Configuration["MongoDb:ConnectionString"] = reviewsDb;

// one DI extension method per layer
builder.Services
    .AddApplication()                             // MediatR + behaviors + validators + AutoMapper
    .AddInfrastructure(builder.Configuration)     // MongoDB, repositories, queries, indexes
    .AddWebApi();                                 // controllers, Swagger, ProblemDetails

builder.Services.AddGrpc(o => o.Interceptors.Add<LoggingInterceptor>());

// Lab 07: business metrics and health check of MongoDB
builder.Services.AddSingleton<ReviewMetrics>();
builder.Services.AddReviewsHealthChecks();

// Lab 08: RabbitMQ and the consumer of the Catalog events (copy of the event title)
builder.AddEventBus();
CatalogEventsConsumer.Register(builder.Services);

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();                       // /health, /alive, /health/live, /health/ready

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Seed test data (idempotent - skipped if the collection is not empty)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Seeding failed - is MongoDB running?");
    }
}

app.MapControllers();
app.MapGrpcService<ReviewsGrpcService>();
app.MapGrpcHealthChecksService();               // grpc.health.v1.Health for the Aggregator

app.Run();
