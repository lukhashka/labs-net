// Configuration only: no business logic here. Runs all resources during development.
var builder = DistributedApplication.CreateBuilder(args);

// The password is a secret parameter: dotnet user-secrets set "Parameters:sql-password" "<password>"
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword).WithDataVolume();
var ordersDb = sql.AddDatabase("OrdersDb");                 // Lab 02
var catalogDb = sql.AddDatabase("CatalogDb");               // Lab 03

var mongo = builder.AddMongoDB("mongo").WithDataVolume();
var reviewsDb = mongo.AddDatabase("ReviewsDb");             // Lab 04

var redis = builder.AddRedis("redis").WithDataVolume();

// Benchmark:CacheEnabled=false switches caches off for a fair comparison of HTTP and gRPC
var cacheEnabled = builder.Configuration["Benchmark:CacheEnabled"] ?? "true";

// The resource name becomes the logical name for service discovery.
// WithHttpHealthCheck (Lab 07): the Dashboard shows Healthy / Degraded / Unhealthy of every service,
// and WaitFor of the next resource waits for a healthy dependency, not only for a started process.
// The https profile: gRPC needs HTTP/2, on Kestrel it is negotiated over TLS (ALPN). REST stays on the same service.
var orders = builder.AddProject<Projects.Orders_Api>("orders-api", launchProfileName: "https")
    .WithReference(ordersDb).WaitFor(ordersDb)
    .WithHttpHealthCheck("/health", endpointName: "http");

var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api", launchProfileName: "https")
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(redis).WaitFor(redis)
    .WithEnvironment("Caching__Enabled", cacheEnabled)
    .WithHttpHealthCheck("/health", endpointName: "http");

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api", launchProfileName: "https")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithHttpHealthCheck("/health", endpointName: "http");

var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(orders).WithReference(catalog).WithReference(reviews)
    .WaitFor(orders).WaitFor(catalog).WaitFor(reviews)
    .WithReference(redis).WaitFor(redis)
    .WithEnvironment("Caching__Enabled", cacheEnabled)
    .WithHttpHealthCheck("/health", endpointName: "http");

builder.AddProject<Projects.Platform_ApiGateway>("gateway")
    .WithHttpEndpoint(port: 5000, name: "public")
    .WithReference(orders).WithReference(catalog).WithReference(reviews)
    .WithReference(aggregator)
    .WaitFor(aggregator)                                      // waits until the Aggregator is Healthy, not only started
    .WithHttpHealthCheck("/health", endpointName: "http");

builder.Build().Run();
