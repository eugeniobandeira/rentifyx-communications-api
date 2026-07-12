using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces;
using RentifyxCommunications.Domain.Interfaces.Common;
using RentifyxCommunications.Domain.Interfaces.Examples;
using RentifyxCommunications.Infrastructure.Context;
using RentifyxCommunications.Infrastructure.Repositories;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime.CredentialManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RentifyxCommunications.IoC;

internal static class InfrastructureDependencyInjection
{
    internal static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext(configuration);
        services.AddRepositories();
        services.AddAwsOptions(configuration);

        return services;
    }

    private static IServiceCollection AddDbContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ExampleRepository>();
        services.AddScoped<IExampleRepository>(sp => sp.GetRequiredService<ExampleRepository>());
        services.AddScoped<IAddRepository<ExampleEntity>>(sp => sp.GetRequiredService<ExampleRepository>());
        services.AddScoped<IGetByIdRepository<ExampleEntity>>(sp => sp.GetRequiredService<ExampleRepository>());
        services.AddScoped<IUpdateRepository<ExampleEntity>>(sp => sp.GetRequiredService<ExampleRepository>());
        services.AddScoped<IDeleteRepository<ExampleEntity>>(sp => sp.GetRequiredService<ExampleRepository>());

        return services;
    }

    private static IServiceCollection AddAwsOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? profile = configuration["AWS:Profile"];
        if (string.IsNullOrWhiteSpace(profile))
            throw new InvalidOperationException(
                "Configuration key 'AWS:Profile' is required. Set it via dotnet user-secrets or a local environment " +
                "variable — it must never be committed. See docs/architecture/overview.md for setup.");

        if (!new CredentialProfileStoreChain().TryGetAWSCredentials(profile, out _))
            throw new InvalidOperationException(
                $"AWS credentials profile '{profile}' was not found. Run 'aws configure --profile {profile}' " +
                "to create it before starting the application.");

        services.AddDefaultAWSOptions(configuration.GetAWSOptions());

        return services;
    }
}
