using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace RentifyxCommunications.Api.Extensions;

internal static class RateLimitExtension
{
    internal const string PolicyName = "fixed";

    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        int permitLimit = configuration.GetValue("RateLimit:PermitLimit", 100);
        int windowSeconds = configuration.GetValue("RateLimit:WindowSeconds", 60);
        int queueLimit = configuration.GetValue("RateLimit:QueueLimit", 0);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(PolicyName, opt =>
            {
                opt.PermitLimit = permitLimit;
                opt.Window = TimeSpan.FromSeconds(windowSeconds);
                opt.QueueLimit = queueLimit;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        return services;
    }

    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
        => app.UseRateLimiter();
}
