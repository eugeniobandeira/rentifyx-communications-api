using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RentifyxCommunications.IoC;

[ExcludeFromCodeCoverage]
public static class DependencyInjectionExtension
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
        => ApplicationDependencyInjection.Register(services);

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
        => InfrastructureDependencyInjection.Register(services, configuration, environment);
}
