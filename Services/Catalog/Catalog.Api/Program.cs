using Catalog.Api.GrpcServices;
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
    builder.AddRedisDistributedCache("redis", configureOptions: o =>
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
builder.Services.AddHealthChecks().AddCheck<CacheHealthCheck>("cache");

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
app.MapDefaultEndpoints();                       // /health and /alive

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

app.MapControllers();
app.MapGrpcService<CatalogGrpcService>();

app.Run();
