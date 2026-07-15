using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using RentifyxCommunications.Api.Extensions.Options;

namespace RentifyxCommunications.Api.Extensions;

internal static class RateLimitExtension
{
    internal const string PolicyName = "fixed";

    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        RateLimitOptions rateLimitOptions = configuration.GetSection("RateLimit").Get<RateLimitOptions>()
            ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(PolicyName, opt =>
            {
                opt.PermitLimit = rateLimitOptions.PermitLimit;
                opt.Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds);
                opt.QueueLimit = rateLimitOptions.QueueLimit;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        return services;
    }

    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
        => app.UseRateLimiter();
}
