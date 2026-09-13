using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PaymentSimulator.Api.Payments;

namespace PaymentSimulator.Api.Endpoints;

internal sealed class PaymentExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            PaymentException paymentException => new ProblemDetails
            {
                Status = paymentException.Error switch
                {
                    PaymentError.InvalidRequest => StatusCodes.Status400BadRequest,
                    PaymentError.OperationConflict => StatusCodes.Status409Conflict,
                    PaymentError.Unavailable => StatusCodes.Status503ServiceUnavailable,
                    _ => throw new ArgumentOutOfRangeException(nameof(exception))
                },
                Title = paymentException.Message
            },
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
}
