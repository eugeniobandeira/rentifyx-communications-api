using RentifyxCommunications.Domain.Constants;
using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace RentifyxCommunications.Api.Middlewares;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const int MaxIdLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = GetOrCreateCorrelationId(context);

        context.Items[CorrelationIdConstants.Key] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty(CorrelationIdConstants.Key, correlationId))
        {
            await next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out StringValues existingId)
            && !string.IsNullOrWhiteSpace(existingId))
        {
            return Sanitize(existingId.ToString());
        }

        return Guid.NewGuid().ToString();
    }

    private static string Sanitize(string value)
    {
        string sanitized = new(value.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return sanitized.Length > MaxIdLength ? sanitized[..MaxIdLength] : sanitized;
    }
}
