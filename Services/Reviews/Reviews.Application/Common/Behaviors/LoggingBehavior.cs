using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Common.Behaviors;

// 1st in the pipeline: logs every request, its result or error
public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {Request}: {@Payload}", name, request);
        try
        {
            var response = await next();
            logger.LogInformation("Handled {Request}", name);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning("{Request} failed with {Error}: {Message}", name, ex.GetType().Name, ex.Message);
            throw;
        }
    }
}
