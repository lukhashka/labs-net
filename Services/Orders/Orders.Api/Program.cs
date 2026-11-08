using Orders.Api.ErrorHandling;
using Orders.Bll.Mapping;
using Orders.Bll.Services;
using Orders.Dal;
using Orders.Dal.Infrastructure;
using Platform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();                    // logging, tracing, health checks, service discovery

var connectionString = builder.Configuration.GetConnectionString("OrdersDb")
    ?? throw new InvalidOperationException("Connection string 'OrdersDb' is missing");

// Scoped: one UnitOfWork and one connection per HTTP request
builder.Services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));
builder.Services.AddSingleton(sp => new DatabaseBootstrapper(
    connectionString, sp.GetRequiredService<ILogger<DatabaseBootstrapper>>()));

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ITicketTypeService, TicketTypeService>();
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<OrdersProfile>());

builder.Services.AddControllers();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();                       // /health and /alive

// Schema, procedures and seed from Lab 01; seed can be switched off with Seeding:Enabled=false
await app.Services.GetRequiredService<DatabaseBootstrapper>().EnsureCreatedAsync(
    app.Configuration.GetValue("Seeding:Enabled", !app.Environment.IsProduction()));

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
