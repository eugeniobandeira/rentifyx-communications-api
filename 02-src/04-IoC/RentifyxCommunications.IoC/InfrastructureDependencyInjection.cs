using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces;
using RentifyxCommunications.Domain.Interfaces.Common;
using RentifyxCommunications.Domain.Interfaces.Examples;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Infrastructure.Context;
using RentifyxCommunications.Infrastructure.Email;
using RentifyxCommunications.Infrastructure.Repositories;
using RentifyxCommunications.Infrastructure.Secrets;
using RentifyxCommunications.Infrastructure.Templates;
using Amazon.DynamoDBv2;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;
using Amazon.SimpleEmail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddSecretsManager();
        services.AddNotificationInfrastructure();

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

    private static IServiceCollection AddSecretsManager(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddAWSService<IAmazonSecretsManager>();
        services.AddSingleton(new SecretsProviderOptions());
        services.AddSingleton<ISecretsProvider, SecretsManagerProvider>();
        services.AddSingleton<SecretsStartupValidator>();

        return services;
    }

    private static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services)
    {
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonSimpleEmailService>();

        services.AddScoped<INotificationRepository, DynamoDbNotificationRepository>();
        services.AddScoped<IConsentRepository, DynamoDbConsentRepository>();
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();

        services.AddScoped<IEmailSender>(sp =>
        {
            IHostEnvironment environment = sp.GetRequiredService<IHostEnvironment>();
            if (!environment.IsProduction())
                return new MockEmailSender();

            return new SesEmailSender(
                sp.GetRequiredService<IAmazonSimpleEmailService>(),
                sp.GetRequiredService<ISecretsProvider>(),
                sp.GetRequiredService<SecretsProviderOptions>());
        });

        return services;
    }
}
