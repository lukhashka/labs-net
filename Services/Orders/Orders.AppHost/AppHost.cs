var builder = DistributedApplication.CreateBuilder(args);

// SQL Server in a container. The database name equals the name of the connection string in Orders.Api,
// so Aspire passes ConnectionStrings:OrdersDb and the code of the API does not change.
var ordersDb = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("OrdersDb");

builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WaitFor(ordersDb);

builder.Build().Run();
