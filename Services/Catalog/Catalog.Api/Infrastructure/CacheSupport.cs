using Catalog.Bll.Caching;
using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.Api.Infrastructure;

// Cache statistics in /health
public class CacheHealthCheck(IMemoryCache memory, CacheMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var stats = memory.GetCurrentStatistics();
        var (l1, l2) = metrics.HitRatios();
        return Task.FromResult(HealthCheckResult.Healthy("Cache statistics", new Dictionary<string, object>
        {
            ["l1.entries"] = stats?.CurrentEntryCount ?? 0,
            ["l1.hitRatio"] = Math.Round(l1, 3),
            ["l2.hitRatio"] = Math.Round(l2, 3)
        }));
    }
}

// Reference data that is requested most often is loaded on start, so the first users do not wait for the database
public class CacheWarmupService(IServiceScopeFactory scopes, ILogger<CacheWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var categories = await scope.ServiceProvider.GetRequiredService<ICategoryService>().GetAllAsync(stoppingToken);
            var firstPage = await scope.ServiceProvider.GetRequiredService<IEventService>().GetListAsync(new EventQuery(), stoppingToken);
            logger.LogInformation("Cache warmed up: {Categories} categories, {Events} events of the first page", categories.Count, firstPage.Items.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache warmup failed, data will be loaded on demand");
        }
    }
}
