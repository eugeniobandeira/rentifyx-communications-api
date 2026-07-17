using System.Threading.RateLimiting;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SimpleEmail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Api.Extensions.Options;
using RentifyxCommunications.Api.Messaging;

namespace RentifyxCommunications.Tests.Integration.Api;

/// <summary>
/// Boots the real <c>Program.cs</c> entry point against LocalStack-backed AWS clients instead of real AWS,
/// and removes the Kafka-dependent hosted services (no broker is available in this test run).
/// <see cref="RateLimitOptions"/> is used as the <c>TEntryPoint</c> marker because <c>Program</c> is
/// <c>internal</c> to the Api assembly and this test project has no <c>InternalsVisibleTo</c> grant for it;
/// <see cref="WebApplicationFactory{TEntryPoint}"/> only needs the marker to resolve the target assembly.
/// </summary>
public sealed class StatusConsentEndpointsWebApplicationFactory : WebApplicationFactory<RateLimitOptions>
{
    private const string FakeAwsProfileName = "localstack-test";
    private const string AwsSharedCredentialsFileEnvironmentVariable = "AWS_SHARED_CREDENTIALS_FILE";

    // Mirrors RateLimitExtension.PolicyName/ConsentPolicyName (internal to the Api assembly, not visible here) -
    // the policy names are route metadata, not behavior, so duplicating the literals is safe.
    private const string FixedRateLimitPolicyName = "fixed";
    private const string ConsentRateLimitPolicyName = "consent";

    // Secret NAMES (not values) - same convention as appsettings.json's "SecretsProvider" section. Exposed so
    // callers can seed matching secrets into their LocalStack Secrets Manager fixture under these names.
    public const string SesArnSecretName = "rentifyx/comms/ses-arn";
    public const string ApiKeySecretName = "rentifyx/comms/api-key";

    private static readonly Type[] KafkaDependentHostedServiceTypes =
    [
        typeof(NotificationRequestedConsumer),
        typeof(DlqObserverHostedService),
        typeof(ReconciliationHostedService),
    ];

    private readonly string _dynamoDbServiceUrl;
    private readonly string _sesServiceUrl;
    private readonly string _secretsManagerServiceUrl;
    private readonly int _consentPermitLimit;
    private readonly string _fakeCredentialsFilePath;

    public StatusConsentEndpointsWebApplicationFactory(
        string dynamoDbServiceUrl,
        string sesServiceUrl,
        string secretsManagerServiceUrl,
        int consentPermitLimit)
    {
        _dynamoDbServiceUrl = dynamoDbServiceUrl;
        _sesServiceUrl = sesServiceUrl;
        _secretsManagerServiceUrl = secretsManagerServiceUrl;
        _consentPermitLimit = consentPermitLimit;

        // AddAwsOptions() (InfrastructureDependencyInjection.cs) fail-fasts unless "AWS:Profile" resolves to a
        // real, locally-known credential profile. We never use these credentials to reach real AWS (all AWS
        // clients are replaced below with LocalStack-pointed instances) - the profile only needs to exist so
        // that startup check passes.
        _fakeCredentialsFilePath = WriteFakeCredentialsFile();
        Environment.SetEnvironmentVariable(AwsSharedCredentialsFileEnvironmentVariable, _fakeCredentialsFilePath);
        AWSConfigs.AWSProfilesLocation = _fakeCredentialsFilePath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AWS:Profile", FakeAwsProfileName);
        builder.UseSetting("AWS:Region", "us-east-1");

        builder.ConfigureTestServices(services =>
        {
            RemoveKafkaDependentHostedServices(services);
            OverrideConsentRateLimitPolicy(services, _consentPermitLimit);

            BasicAWSCredentials fakeCredentials = new("test", "test");

            services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient(
                fakeCredentials,
                new AmazonDynamoDBConfig { ServiceURL = _dynamoDbServiceUrl }));

            services.AddSingleton<IAmazonSimpleEmailService>(_ => new AmazonSimpleEmailServiceClient(
                fakeCredentials,
                new AmazonSimpleEmailServiceConfig { ServiceURL = _sesServiceUrl }));

            services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient(
                fakeCredentials,
                new AmazonSecretsManagerConfig { ServiceURL = _secretsManagerServiceUrl }));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_fakeCredentialsFilePath))
            File.Delete(_fakeCredentialsFilePath);
    }

    private static void RemoveKafkaDependentHostedServices(IServiceCollection services)
    {
        // Program.cs registers exactly 6 IHostedService implementations. Three (NotificationRequestedConsumer,
        // DlqObserverHostedService, ReconciliationHostedService) are registered via AddHostedService<T>()
        // (ImplementationType is set). The other three are RetryTopicConsumer, registered three times via the
        // AddHostedService(sp => new RetryTopicConsumer(topic, ...)) factory overload (ImplementationType is
        // null, only ImplementationFactory is set) - factory-based IHostedService registration is unique to
        // RetryTopicConsumer in this app, so filtering on "any factory-based IHostedService" safely captures
        // all three without needing to invoke the factory to inspect its return type.
        ServiceDescriptor[] descriptorsToRemove = [.. services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && (descriptor.ImplementationFactory is not null
                || (descriptor.ImplementationType is not null
                    && KafkaDependentHostedServiceTypes.Contains(descriptor.ImplementationType))))];

        foreach (ServiceDescriptor descriptor in descriptorsToRemove)
            services.Remove(descriptor);
    }

    /// <summary>
    /// Program's own AddRateLimiting(configuration) reads "RateLimit:Consent:PermitLimit" once, at service
    /// registration time, and registers the "consent" policy under that name via a single
    /// <see cref="IConfigureOptions{RateLimiterOptions}"/> delegate. IConfiguration-based overrides (UseSetting,
    /// ConfigureAppConfiguration) can't reliably beat appsettings.json for a minimal-hosting Program.cs, and
    /// re-adding the "consent" policy via a second Configure&lt;RateLimiterOptions&gt; throws ArgumentException
    /// ("policy name already added") once both delegates run. Removing Program's own configure delegate and
    /// replacing it with a test-only one - the same DI-manipulation pattern <see cref="RemoveKafkaDependentHostedServices"/>
    /// already uses - sidesteps both problems entirely.
    /// </summary>
    private static void OverrideConsentRateLimitPolicy(IServiceCollection services, int consentPermitLimit)
    {
        services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();

        services.Configure<RateLimiterOptions>(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // "fixed" is applied to every endpoint (EndpointExtensions.cs), so it must exist even though no
            // test in this class fires enough requests to trip it - only its default (100/60s) is needed here.
            options.AddFixedWindowLimiter(FixedRateLimitPolicyName, opt =>
            {
                opt.PermitLimit = 100;
                opt.Window = TimeSpan.FromSeconds(60);
                opt.QueueLimit = 0;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
            options.AddFixedWindowLimiter(ConsentRateLimitPolicyName, opt =>
            {
                opt.PermitLimit = consentPermitLimit;
                opt.Window = TimeSpan.FromSeconds(60);
                opt.QueueLimit = 0;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });
    }

    private static string WriteFakeCredentialsFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rentifyx-fake-aws-credentials-{Guid.NewGuid():N}");
        File.WriteAllText(
            path,
            $"[{FakeAwsProfileName}]{Environment.NewLine}" +
            $"aws_access_key_id = test{Environment.NewLine}" +
            $"aws_secret_access_key = test{Environment.NewLine}");

        return path;
    }
}
