// Configuration only: no business logic here. Runs all resources during development.
var builder = DistributedApplication.CreateBuilder(args);

// The password is a secret parameter: dotnet user-secrets set "Parameters:sql-password" "<password>"
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword).WithDataVolume();
var ordersDb = sql.AddDatabase("OrdersDb");                 // Lab 02
var catalogDb = sql.AddDatabase("CatalogDb");               // Lab 03

var mongo = builder.AddMongoDB("mongo").WithDataVolume();
var reviewsDb = mongo.AddDatabase("ReviewsDb");             // Lab 04

// The resource name becomes the logical name for service discovery
var orders = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb).WaitFor(ordersDb)
    .WithHttpHealthCheck("/health");

var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithHttpHealthCheck("/health");

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithHttpHealthCheck("/health");

var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(orders).WithReference(catalog).WithReference(reviews)
    .WaitFor(orders).WaitFor(catalog).WaitFor(reviews)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Platform_ApiGateway>("gateway")
    .WithHttpEndpoint(port: 5000, name: "public")
    .WithReference(orders).WithReference(catalog).WithReference(reviews)
    .WithReference(aggregator)
    .WaitFor(aggregator);

builder.Build().Run();
