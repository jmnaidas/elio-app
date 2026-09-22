using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Elio.Application.Identity;

namespace Elio.Api.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is RequestFailure failure)
        {
            context.Response.StatusCode = failure.Status;
            var detail = new ProblemDetails { Status = failure.Status, Title = failure.Message };
            if (failure.Errors is not null) detail.Extensions["errors"] = failure.Errors;
            return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
        }
        logger.LogError(exception, "Unhandled request failure");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "Please retry or contact support with the correlation ID."
            }
        });
    }
}
