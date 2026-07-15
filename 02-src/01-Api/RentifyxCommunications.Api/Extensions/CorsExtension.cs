using RentifyxCommunications.Api.Extensions.Options;

namespace RentifyxCommunications.Api.Extensions;

internal static class CorsExtension
{
    private const string PolicyName = "DefaultCorsPolicy";

    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        CorsOptions corsOptions = configuration.GetSection("Cors").Get<CorsOptions>()
            ?? throw new InvalidOperationException("Cors is not configured.");

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                policy.WithOrigins(corsOptions.AllowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()
                      .WithExposedHeaders("X-Correlation-Id")
                      .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        return services;
    }

    public static IApplicationBuilder UseCorsPolicy(this IApplicationBuilder app)
        => app.UseCors(PolicyName);
}
