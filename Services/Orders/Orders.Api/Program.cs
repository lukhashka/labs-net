using Orders.Api.ErrorHandling;
using Orders.Api.GrpcServices;
using Orders.Api.HealthChecks;
using Orders.Api.IntegrationEvents;
using Orders.Api.Metrics;
using Orders.Api.Security;
using Platform.ServiceDefaults.Authentication;
using Orders.Bll.Mapping;
using Orders.Bll.Services;
using Orders.Dal;
using Orders.Dal.Infrastructure;
using Platform.EventBus;
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

// Lab 07: business metrics (singleton for the whole lifetime) and health checks of the database
builder.Services.AddSingleton<OrderStatusSnapshot>();
builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddHostedService<OrderStatusRefresher>();
builder.Services.AddOrdersHealthChecks(connectionString);

// Lab 08: RabbitMQ (AddRabbitMQClient("messaging") inside) and the consumer of the Catalog events
builder.AddEventBus();
CatalogEventsConsumer.Register(builder.Services);

// Lab 09: tokens of the Auth Service, IdentityServer (audience orders_api) and Keycloak (audience orders-api).
// The service validates every token locally and checks the permission of the endpoint itself.
builder.AddPlatformAuthentication(identityServerAudience: "orders_api")
    .AddKeycloakJwtBearer(serviceName: "keycloak", realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.Keycloak, o =>
        {
            o.Audience = "orders-api";
            o.MapInboundClaims = false;
            if (builder.Environment.IsDevelopment())
                o.RequireHttpsMetadata = false;            // Keycloak runs over HTTP in development
        });
builder.Services.AddPlatformAuthorization();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddGrpc(o => o.Interceptors.Add<LoggingInterceptor>());
builder.Services.AddControllers();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();                       // /health, /alive, /health/live, /health/ready

// Schema, procedures and seed from Lab 01; seed can be switched off with Seeding:Enabled=false
await app.Services.GetRequiredService<DatabaseBootstrapper>().EnsureCreatedAsync(
    app.Configuration.GetValue("Seeding:Enabled", !app.Environment.IsProduction()));

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<OrdersGrpcService>();
app.MapGrpcHealthChecksService();               // grpc.health.v1.Health for the Aggregator

app.Run();
