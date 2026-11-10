using Platform.IdentityServer;

var builder = WebApplication.CreateBuilder(args);

var app = builder
    .ConfigureServices()
    .ConfigurePipeline();

await app.SeedAsync();

app.Run();
