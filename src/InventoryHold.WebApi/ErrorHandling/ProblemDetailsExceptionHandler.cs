using InventoryHold.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace InventoryHold.WebApi.ErrorHandling;

/// <summary>The single place that maps exceptions to RFC 7807 responses.</summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // The client has gone, so there is nobody to answer.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return true;

        var problem = ToProblem(exception);
        if (problem.Status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Request failed: {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails ToProblem(Exception exception) => exception switch
    {
        HoldValidationException e => new ValidationProblemDetails(e.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid hold request",
            Detail = e.Message
        },
        HoldNotFoundException e => Problem(StatusCodes.Status404NotFound, "Hold not found", e, "hold_not_found"),
        ProductNotFoundException e => WithExtension(
            Problem(StatusCodes.Status404NotFound, "Product not found", e, "product_not_found"), "skus", e.Skus),
        InsufficientStockException e => WithExtension(WithExtension(
            Problem(StatusCodes.Status409Conflict, "Insufficient stock", e, "insufficient_stock"), "sku", e.Sku), "requested", e.Requested),
        HoldAlreadyReleasedException e => Problem(StatusCodes.Status409Conflict, "Hold already released", e, "hold_already_released"),
        HoldExpiredException e => Problem(StatusCodes.Status410Gone, "Hold expired", e, "hold_expired"),
        MongoConnectionException or TimeoutException => new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Service temporarily unavailable",
            Detail = "The data store could not be reached. Please retry shortly."
        },
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Unexpected error",
            Detail = "An unexpected error occurred."
        }
    };

    private static ProblemDetails Problem(int status, string title, Exception exception, string code) =>
        WithExtension(new ProblemDetails { Status = status, Title = title, Detail = exception.Message }, "code", code);

    private static ProblemDetails WithExtension(ProblemDetails problem, string key, object value)
    {
        problem.Extensions[key] = value;
        return problem;
    }
}
