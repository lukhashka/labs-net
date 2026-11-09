using Platform.ServiceDefaults;
using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using Reviews.WebApi.GrpcServices;
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

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();                       // /health and /alive

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

app.Run();
