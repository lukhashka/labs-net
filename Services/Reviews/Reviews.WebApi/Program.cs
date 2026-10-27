using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using Reviews.WebApi.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()                             // MediatR + behaviors + validators
    .AddInfrastructure(builder.Configuration);    // MongoDB, repositories, queries, indexes

// Validation is done by FluentValidation in the MediatR pipeline,
// so MVC must not reject null strings before the request reaches ValidationBehavior
builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

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
