using FlashSale.Application.Orders.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Errors;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            OrderRequestException orderException => CreateOrderProblem(orderException),
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read."
            },
            _ => null
        };
        if (problem is null)
        {
            return false;
        }

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        });
    }

    private static ProblemDetails CreateOrderProblem(OrderRequestException exception)
    {
        var (status, code) = exception.Error switch
        {
            OrderErrorCode.InvalidRequest => (StatusCodes.Status400BadRequest, "invalid_request"),
            OrderErrorCode.UnknownProduct => (StatusCodes.Status404NotFound, "unknown_product"),
            OrderErrorCode.InsufficientInventory => (StatusCodes.Status409Conflict, "insufficient_inventory"),
            OrderErrorCode.IdempotencyConflict => (StatusCodes.Status409Conflict, "idempotency_conflict"),
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };
        return new ProblemDetails
        {
            Status = status,
            Title = exception.Message,
            Extensions = { ["code"] = code }
        };
    }
}
