using MaintainXpert.Assets.Domain;
using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MaintainXpert.Api.Infrastructure;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            BadHttpRequestException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Malformed request",
                Detail = "The request body could not be parsed."
            },
            WorkOrderNotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Work order not found",
                Detail = exception.Message
            },
            AssetNotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Asset not found",
                Detail = exception.Message
            },
            AssetDecommissionedException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Asset decommissioned",
                Detail = exception.Message
            },
            InvalidWorkOrderTransitionException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Invalid work order transition",
                Detail = exception.Message
            },
            InvalidAssetOperationException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Invalid asset operation",
                Detail = exception.Message
            },
            ArgumentException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = exception.Message
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            }
        };

        if (problemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        problemDetails.Instance = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
