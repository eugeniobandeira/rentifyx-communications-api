using Asp.Versioning;

namespace RentifyxCommunications.Api.Extensions;

internal static class VersioningExtension
{
    public static IServiceCollection AddVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        });

        return services;
    }

    public static RouteGroupBuilder MapVersionedApi(this IEndpointRouteBuilder app, int major, int minor = 0)
    {
        return app.NewVersionedApi()
                  .MapGroup($"/api/v{major}")
                  .HasApiVersion(new ApiVersion(major, minor));
    }
}
