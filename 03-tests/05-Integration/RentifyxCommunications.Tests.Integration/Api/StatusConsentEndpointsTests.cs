using System.Net;
using System.Net.Http.Json;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Tests.Integration.Infrastructure;
using RentifyxCommunications.Tests.Integration.Secrets;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Api;

/// <summary>
/// End-to-end tests for the four HTTP status/consent endpoints, exercised through the real ASP.NET Core
/// pipeline (auth, rate limiting, routing, handlers) via <see cref="StatusConsentEndpointsWebApplicationFactory"/>,
/// backed by LocalStack for DynamoDB/SES/Secrets Manager instead of real AWS, and with the Kafka-dependent
/// hosted services removed since no broker is available in this test run.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class StatusConsentEndpointsTests : IClassFixture<StatusConsentEndpointsTests.SecretsFixture>, IAsyncLifetime
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const int ConsentPermitLimit = 2;

    private readonly LocalStackNotificationInfrastructureFixture _notificationFixture;
    private readonly SecretsFixture _secretsFixture;

    private StatusConsentEndpointsWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public StatusConsentEndpointsTests(
        LocalStackNotificationInfrastructureFixture notificationFixture,
        SecretsFixture secretsFixture)
    {
        _notificationFixture = notificationFixture;
        _secretsFixture = secretsFixture;
    }

    public Task InitializeAsync()
    {
        _factory = new StatusConsentEndpointsWebApplicationFactory(
            dynamoDbServiceUrl: _notificationFixture.DynamoDb.Config.ServiceURL,
            sesServiceUrl: _notificationFixture.Ses.Config.ServiceURL,
            secretsManagerServiceUrl: _secretsFixture.SecretsManager.Client.Config.ServiceURL,
            consentPermitLimit: ConsentPermitLimit);

        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();

        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetNotificationStatus_WithoutApiKey_ReturnsUnauthorized()
    {
        using HttpResponseMessage response = await _client.GetAsync($"api/v1/notifications/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetNotificationsByRecipient_WithoutApiKey_ReturnsUnauthorized()
    {
        using HttpResponseMessage response = await _client.GetAsync($"api/v1/notifications/recipient/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConsent_WithoutApiKey_ReturnsUnauthorized()
    {
        using HttpResponseMessage response = await _client.GetAsync($"api/v1/consent/{Guid.NewGuid()}?channel=Email");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateConsent_WithoutApiKey_ReturnsUnauthorized()
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"api/v1/consent/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { channel = "Email", optedIn = false })
        };

        using HttpResponseMessage response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateConsent_WithValidApiKey_Succeeds_AndWritesQueryableAuditRecord()
    {
        Guid recipientId = Guid.NewGuid();

        using HttpResponseMessage response = await SendUpdateConsentAsync(recipientId, optedIn: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        QueryResponse auditQuery = await _notificationFixture.DynamoDb.QueryAsync(new QueryRequest
        {
            TableName = LocalStackNotificationInfrastructureFixture.TableName,
            KeyConditionExpression =
                $"{NotificationTableSchema.PartitionKey} = :pk AND begins_with({NotificationTableSchema.SortKey}, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"{NotificationTableSchema.ConsentPartitionKeyPrefix}{recipientId}"),
                [":skPrefix"] = new(NotificationTableSchema.ConsentAuditSortKeyPrefix)
            }
        });

        auditQuery.Items.Should().ContainSingle();
        auditQuery.Items[0]["RecipientId"].S.Should().Be(recipientId.ToString());
    }

    [Fact]
    public async Task UpdateConsent_BeyondConsentPermitLimit_ReturnsTooManyRequests()
    {
        // ConsentPermitLimit (2) is configured well below the default "fixed" policy's 100-permit limit
        // (appsettings.json RateLimit:PermitLimit), so firing a handful of requests past 2 only proves a 429
        // if the stricter, consent-specific policy is actually the one enforced on this route.
        const int requestsToFire = ConsentPermitLimit + 5;

        List<HttpStatusCode> statusCodes = [];
        for (int i = 0; i < requestsToFire; i++)
        {
            using HttpResponseMessage response = await SendUpdateConsentAsync(Guid.NewGuid(), optedIn: true);
            statusCodes.Add(response.StatusCode);
        }

        statusCodes.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    private async Task<HttpResponseMessage> SendUpdateConsentAsync(Guid recipientId, bool optedIn)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"api/v1/consent/{recipientId}")
        {
            Content = JsonContent.Create(new { channel = "Email", optedIn })
        };
        request.Headers.Add(ApiKeyHeaderName, _secretsFixture.ApiKeyValue);

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Owns a dedicated LocalStack Secrets Manager container for this test class (separate from the
    /// notification/SES LocalStack container shared via <see cref="NotificationInfrastructureFixtureGroup"/>,
    /// since a test class can only belong to one xUnit collection). Seeds the 4 secrets
    /// <c>SecretsStartupValidator</c> requires at boot, using the same secret names configured in
    /// appsettings.json's "SecretsProvider" section, so no config override is needed for those.
    /// </summary>
    public sealed class SecretsFixture : IAsyncLifetime
    {
        public LocalStackSecretsManagerFixture SecretsManager { get; } = new();

        public string ApiKeyValue { get; } = $"test-api-key-{Guid.NewGuid():N}";

        public async Task InitializeAsync()
        {
            await SecretsManager.InitializeAsync();

            await SecretsManager.SeedSecretAsync(
                StatusConsentEndpointsWebApplicationFactory.SesArnSecretName,
                "arn:aws:ses:us-east-1:000000000000:identity/test@example.com");
            await SecretsManager.SeedSecretAsync(
                StatusConsentEndpointsWebApplicationFactory.ApiKeySecretName,
                ApiKeyValue);
        }

        public Task DisposeAsync() => SecretsManager.DisposeAsync();
    }
}
