using System.Reflection;
using RentifyxCommunications.Api.Abstract;

namespace RentifyxCommunications.Api.Extensions;

internal static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services)
    {
        IEnumerable<Type> endpointTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.IsAssignableTo(typeof(IEndpoint)));

        foreach (Type type in endpointTypes)
            services.AddTransient(typeof(IEndpoint), type);

        return services;
    }

    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder v1 = app.MapVersionedApi(1)
                                  .RequireRateLimiting(RateLimitExtension.PolicyName);

        IEnumerable<IEndpoint> endpoints = app.ServiceProvider
            .GetServices<IEndpoint>();

        foreach (IEndpoint endpoint in endpoints)
            endpoint.MapEndpoint(v1);

        return app;
    }
}
