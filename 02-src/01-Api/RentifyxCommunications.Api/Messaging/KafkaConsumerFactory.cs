using Amazon;
using AWS.MSK.Auth;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;

namespace RentifyxCommunications.Api.Messaging;

internal sealed class KafkaConsumerFactory(
    IConfiguration configuration,
    IOptions<KafkaOptions> kafkaOptions,
    IHostEnvironment environment) : IKafkaConsumerFactory
{
    private static readonly AWSMSKAuthTokenGenerator TokenGenerator = new();

    public IConsumer<Ignore, string> Create()
    {
        string bootstrapServers = configuration.GetConnectionString("kafka")
            ?? throw new InvalidOperationException("Connection string 'kafka' not found.");

        ConsumerConfig config = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = kafkaOptions.Value.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        // Local dev (Aspire's Kafka container) is plaintext, no auth. MSK
        // Serverless in production requires SASL/IAM - see rentifyx-platform
        // ADR-002. No static credentials: the token is a short-lived AWS
        // SigV4 signature generated from the EC2 instance role, refreshed by
        // Confluent.Kafka via the callback below whenever it's about to expire.
        if (!environment.IsProduction())
            return new ConsumerBuilder<Ignore, string>(config).Build();

        config.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.SaslMechanism = SaslMechanism.OAuthBearer;

        RegionEndpoint region = RegionEndpoint.GetBySystemName(
            configuration["AWS:Region"] ?? throw new InvalidOperationException("Configuration 'AWS:Region' not found."));

        return new ConsumerBuilder<Ignore, string>(config)
            .SetOAuthBearerTokenRefreshHandler((client, _) =>
            {
                try
                {
                    (string token, long expiryMs) = TokenGenerator.GenerateAuthToken(region);
                    client.OAuthBearerSetToken(token, expiryMs, "rentifyx-communications-api");
                }
                catch (Exception ex)
                {
                    client.OAuthBearerSetTokenFailure(ex.ToString());
                }
            })
            .Build();
    }
}
