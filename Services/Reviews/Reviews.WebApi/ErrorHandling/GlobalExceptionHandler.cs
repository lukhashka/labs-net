using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Reviews.Domain.Exceptions;

namespace Reviews.WebApi.ErrorHandling;

// Converts exceptions from the MediatR pipeline into RFC 7807 ProblemDetails
public class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            DomainException => (StatusCodes.Status400BadRequest, "Business rule violated"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey }
                => (StatusCodes.Status409Conflict, "Duplicate key"),
            MongoConnectionException or TimeoutException
                => (StatusCodes.Status503ServiceUnavailable, "Database is unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Server error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // do not show internal details for 500/503
            Detail = status >= 500 ? null : ex.Message,
            Instance = http.Request.Path
        };

        // All validation errors together: { "Rating": ["..."], "Text": ["..."] }
        if (ex is ValidationException validation)
        {
            problem.Detail = "One or more validation errors occurred";
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = ex
        });
    }
}
