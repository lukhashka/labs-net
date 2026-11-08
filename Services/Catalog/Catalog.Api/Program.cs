using Catalog.Api.Middleware;
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

// BLL
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ITicketTypeService, TicketTypeService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<CatalogProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<CreateEventDtoValidator>();

// API
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

app.Run();
