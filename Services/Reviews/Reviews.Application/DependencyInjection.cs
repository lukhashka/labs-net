using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Common.Behaviors;
using Reviews.Application.Common.Mappings;

namespace Reviews.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // MediatR 12.5.0 - last Apache-2.0 version, no license key needed
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));     // 1 - outer
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));  // 2
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>)); // 3 - closest to handler
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddAutoMapper(cfg => cfg.AddProfile<ReviewsProfile>());
        return services;
    }
}
