using Reviews.WebApi.ErrorHandling;

namespace Reviews.WebApi;

// DI method of the presentation layer (like AddApplication / AddInfrastructure for other layers)
public static class DependencyInjection
{
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        // Validation is done by FluentValidation in the MediatR pipeline,
        // so MVC must not reject null strings before the request reaches ValidationBehavior
        services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
