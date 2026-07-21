using RentifyxCommunications.Api.Extensions.Options;

namespace RentifyxCommunications.Api.Extensions;

internal static class CorsExtension
{
    private const string PolicyName = "DefaultCorsPolicy";

    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                // TEMPORARY (2026-07-20): accepting any origin until the
                // real frontend origin is known. Revert to the commented
                // block below (Cors:AllowedOrigins allowlist) once it is.
#pragma warning disable S125
                // CorsOptions corsOptions = configuration.GetSection("Cors").Get<CorsOptions>()
                //     ?? throw new InvalidOperationException("Cors is not configured.");
                // policy.WithOrigins(corsOptions.AllowedOrigins)
                //       .AllowAnyHeader()
                //       .AllowAnyMethod()
                //       .AllowCredentials()
                //       .WithExposedHeaders("X-Correlation-Id")
                //       .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
#pragma warning restore S125

                // SetIsOriginAllowed(_ => true) reflects any request Origin
                // back - works with AllowCredentials() (AllowAnyOrigin() does not).
                policy.SetIsOriginAllowed(_ => true)
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
