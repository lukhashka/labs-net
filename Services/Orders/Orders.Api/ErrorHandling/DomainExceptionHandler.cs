using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Domain.Exceptions;

namespace Orders.Api.ErrorHandling;

// The last link of the chain "procedure -> DAL -> BLL -> HTTP status". No try/catch in controllers.
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Business rule conflict"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation error"),
            _ => (StatusCodes.Status500InternalServerError, "Server error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");
        else
            logger.LogWarning("{ExceptionType}: {Reason}", ex.GetType().Name, ex.Message);

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // details of an unexpected error stay in the log outside Development
                Detail = status < 500 || environment.IsDevelopment() ? ex.Message : null
            }
        });
    }
}
