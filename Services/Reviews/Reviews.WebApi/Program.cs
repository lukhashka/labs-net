using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using Reviews.WebApi;

var builder = WebApplication.CreateBuilder(args);

// one DI extension method per layer
builder.Services
    .AddApplication()                             // MediatR + behaviors + validators + AutoMapper
    .AddInfrastructure(builder.Configuration)     // MongoDB, repositories, queries, indexes
    .AddWebApi();                                 // controllers, Swagger, ProblemDetails

var app = builder.Build();

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

app.Run();
