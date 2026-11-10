using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        MongoMappings.Register(); // must run before the first use of the driver

        services.Configure<MongoDbSettings>(config.GetSection(MongoDbSettings.SectionName));
        // One IMongoClient in DI: the context and the health check of MongoDB use the same connection pool
        services.AddSingleton<IMongoClient>(sp => MongoDbContext.CreateClient(sp.GetRequiredService<IOptions<MongoDbSettings>>().Value));
        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<ICatalogReplica, CatalogReplica>();            // Lab 08

        services.AddScoped<IDataSeeder, ReviewSeeder>();
        services.AddHostedService<IndexCreationService>();

        return services;
    }
}
