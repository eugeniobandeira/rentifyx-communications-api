using System.Diagnostics.CodeAnalysis;
using RentifyxCommunications.Api.Middlewares;

namespace RentifyxCommunications.Api.Extensions;

[ExcludeFromCodeCoverage]
public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
