using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RentifyxCommunications.Domain.Constants;

namespace RentifyxCommunications.Api.Middlewares;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    private const int StatusClientClosedRequest = 499;
    private const string ProblemDetailsContentType = "application/problem+json";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string? correlationId = httpContext.Items[CorrelationIdConstants.Key]?.ToString();
        string traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                "Request cancelled by client. Path={Path} CorrelationId={CorrelationId} TraceId={TraceId}",
                httpContext.Request.Path,
                correlationId,
                traceId);
            httpContext.Response.StatusCode = StatusClientClosedRequest;
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            logger.LogWarning(
                "Response already started, cannot write error. Path={Path} CorrelationId={CorrelationId} TraceId={TraceId}",
                httpContext.Request.Path,
                correlationId,
                traceId);
            return true;
        }

        logger.LogError(
            exception,
            "Unhandled exception. Path={Path} CorrelationId={CorrelationId} TraceId={TraceId}",
            httpContext.Request.Path,
            correlationId,
            traceId);

        ProblemDetails problem = new()
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred. Please try again later.",
            Instance = httpContext.Request.Path,
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["traceId"] = traceId
            }
        };

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: ProblemDetailsContentType,
            cancellationToken: cancellationToken);

        return true;
    }
}
