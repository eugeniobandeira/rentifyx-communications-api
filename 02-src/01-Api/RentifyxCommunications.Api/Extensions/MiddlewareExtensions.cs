using RentifyxCommunications.Api.Middlewares;

namespace RentifyxCommunications.Api.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
