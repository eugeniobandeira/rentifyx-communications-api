using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Infrastructure.Email;
using RentifyxCommunications.Infrastructure.Messaging;
using RentifyxCommunications.Infrastructure.Options;
using RentifyxCommunications.Infrastructure.Repositories.Notifications;
using RentifyxCommunications.Infrastructure.Resilience;
using RentifyxCommunications.Infrastructure.Secrets;
using RentifyxCommunications.Infrastructure.Templates;
using Amazon.DynamoDBv2;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;
using Amazon.SimpleEmail;
using ErrorOr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;

namespace RentifyxCommunications.IoC;

internal static class InfrastructureDependencyInjection
{
    internal static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAwsOptions(configuration);
        services.AddSecretsManager(configuration);
        services.AddNotificationInfrastructure(configuration);
        services.AddMessaging(configuration);

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

    private static IServiceCollection AddSecretsManager(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddAWSService<IAmazonSecretsManager>();
        services.Configure<SecretsProviderOptions>(configuration.GetSection("SecretsProvider"));
        services.AddSingleton<ISecretsProvider, SecretsManagerProvider>();
        services.AddSingleton<SecretsStartupValidator>();

        return services;
    }

    private static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonSimpleEmailService>();

        services.AddScoped<INotificationRepository, DynamoDbNotificationRepository>();
        services.AddScoped<IConsentRepository, DynamoDbConsentRepository>();
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();

        services.Configure<ResilienceOptions>(configuration.GetSection("Resilience"));
        services.AddSingleton<ResilienceStartupValidator>();
        services.AddSingleton(sp =>
            ResiliencePipelineFactory.Create(sp.GetRequiredService<IOptions<ResilienceOptions>>().Value));

        services.AddScoped<IEmailSender>(sp =>
        {
            IHostEnvironment environment = sp.GetRequiredService<IHostEnvironment>();
            IEmailSender innerSender = environment.IsProduction()
                ? new SesEmailSender(
                    sp.GetRequiredService<IAmazonSimpleEmailService>(),
                    sp.GetRequiredService<ISecretsProvider>(),
                    sp.GetRequiredService<IOptions<SecretsProviderOptions>>().Value)
                : new MockEmailSender();

            return new ResilientEmailSender(innerSender, sp.GetRequiredService<ResiliencePipeline<ErrorOr<Success>>>());
        });

        return services;
    }

    private static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IKafkaProducerFactory, KafkaProducerFactory>();
        services.AddScoped<IFailureRouter, KafkaFailureRouter>();

        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.Configure<ReconciliationOptions>(configuration.GetSection("Reconciliation"));

        return services;
    }
}
