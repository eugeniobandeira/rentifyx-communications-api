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
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Infrastructure.Email;
using RentifyxCommunications.Infrastructure.Messaging;
using RentifyxCommunications.Infrastructure.Options;
using RentifyxCommunications.Infrastructure.Repositories.Notifications;
using RentifyxCommunications.Infrastructure.Resilience;
using RentifyxCommunications.Infrastructure.Secrets;
using RentifyxCommunications.Infrastructure.Templates;

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
        services.AddBoundOptions<SecretsProviderOptions>(configuration, "SecretsProvider");
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

        services.AddBoundOptions<DynamoDbOptions>(configuration, "DynamoDb");
        services.AddScoped<INotificationRepository, DynamoDbNotificationRepository>();
        services.AddScoped<IConsentRepository, DynamoDbConsentRepository>();
        services.AddScoped<IConsentAuditRepository, DynamoDbConsentAuditRepository>();
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();

        services.AddBoundOptions<ResilienceOptions>(configuration, "Resilience");
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

        services.AddBoundOptions<KafkaOptions>(configuration, "Kafka");
        services.AddBoundOptions<ReconciliationOptions>(configuration, "Reconciliation");

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> for an options record by binding it via
    /// <see cref="ConfigurationBinder.Get{T}(IConfiguration)"/> (constructor-argument binding) instead of
    /// <c>services.Configure&lt;TOptions&gt;(section)</c>. The latter relies on
    /// <see cref="IOptionsFactory{TOptions}"/> calling <c>Activator.CreateInstance&lt;TOptions&gt;()</c> before
    /// binding config onto the result, which throws <see cref="MissingMethodException"/> for any options
    /// record here - none has a public parameterless constructor, since C# does not emit one for a positional
    /// record just because every parameter has a default value.
    /// </summary>
    private static IServiceCollection AddBoundOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.AddSingleton<IOptions<TOptions>>(_ => Options.Create(
            configuration.GetSection(sectionName).Get<TOptions>()
            ?? throw new InvalidOperationException($"Configuration section '{sectionName}' is required.")));

        return services;
    }
}
