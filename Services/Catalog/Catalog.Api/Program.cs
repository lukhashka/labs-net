using Catalog.Api.GrpcServices;
using Catalog.Api.HealthChecks;
using Catalog.Api.IntegrationEvents;
using Catalog.Api.Metrics;
using Catalog.Api.Infrastructure;
using Catalog.Api.Middleware;
using Catalog.Bll.Caching;
using Platform.ServiceDefaults;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Bll.Validation;
using Catalog.Dal;
using Catalog.Dal.Seeding;
using FluentValidation;
using Platform.EventBus;
using Platform.ServiceDefaults.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();                    // logging, tracing, health checks, service discovery

// DAL: connection string from appsettings, can be overridden by ConnectionStrings__CatalogDb
builder.Services.AddDbContext<CatalogDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("CatalogDb")));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Cache: L1 in memory, L2 in Redis (without Aspire - in memory of the process)
builder.Services.AddMemoryCache(o =>
{
    o.SizeLimit = 10_000;                            // counted in Size of entries, every entry has Size = 1
    o.TrackStatistics = true;
});
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis",
        configureSettings: s => s.DisableHealthChecks = true,   // own check with Degraded, see HealthChecks/
        configureOptions: o =>
    {
        o.ConnectTimeout = 1000;
        o.SyncTimeout = 1000;
        o.AsyncTimeout = 1000;
    });
else
    builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<CacheMetrics>();
builder.Services.AddSingleton<ITwoLevelCache, TwoLevelCache>();
builder.Services.AddHostedService<CacheWarmupService>();

// Lab 07: business metrics and health checks (database Unhealthy, Redis Degraded)
builder.Services.AddSingleton<CatalogMetrics>();
builder.Services.AddCatalogHealthChecks(builder.Configuration);

// Lab 08: RabbitMQ connection, publisher and the background sender of the outbox
builder.AddEventBus();
builder.Services.AddHostedService<OutboxPublisher>();

// Lab 09: tokens of the Auth Service, IdentityServer (audience products_api) and Keycloak (audience products-api).
// The service validates every token locally and checks the permission of the endpoint itself.
builder.AddPlatformAuthentication(identityServerAudience: "products_api")
    .AddKeycloakJwtBearer(serviceName: "keycloak", realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.Keycloak, o =>
        {
            o.Audience = "products-api";
            o.MapInboundClaims = false;
            if (builder.Environment.IsDevelopment())
                o.RequireHttpsMetadata = false;            // Keycloak runs over HTTP in development
        });
builder.Services.AddPlatformAuthorization();

// BLL
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ITicketTypeService, TicketTypeService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<CatalogProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<CreateEventDtoValidator>();

// API
builder.Services.AddGrpc(o => o.Interceptors.Add<LoggingInterceptor>());
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();                       // /health, /alive, /health/live, /health/ready

// migrations + idempotent seed; seed can be switched off (e.g. in Production) with Seeding:Enabled=false
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue("Seeding:Enabled", !app.Environment.IsProduction()))
        await DbInitializer.SeedAsync(db, app.Logger);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<CatalogGrpcService>();
app.MapGrpcHealthChecksService();               // grpc.health.v1.Health for the Aggregator

app.Run();
