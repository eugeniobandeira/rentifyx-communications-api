using Amazon;
using AWS.MSK.Auth;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace RentifyxCommunications.Infrastructure.Messaging;

public sealed class KafkaProducerFactory(
    IConfiguration configuration,
    IHostEnvironment environment) : IKafkaProducerFactory
{
    private static readonly AWSMSKAuthTokenGenerator TokenGenerator = new();

    public IProducer<Null, string> Create()
    {
        string bootstrapServers = configuration.GetConnectionString("kafka")
            ?? throw new InvalidOperationException("Connection string 'kafka' not found.");

        ProducerConfig config = new()
        {
            BootstrapServers = bootstrapServers
        };

        // Local dev (Aspire's Kafka container) is plaintext, no auth. MSK
        // Serverless in production requires SASL/IAM - see rentifyx-platform
        // ADR-002. No static credentials: the token is a short-lived AWS
        // SigV4 signature generated from the EC2 instance role, refreshed by
        // Confluent.Kafka via the callback below whenever it's about to expire.
        if (!environment.IsProduction())
            return new ProducerBuilder<Null, string>(config).Build();

        config.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.SaslMechanism = SaslMechanism.OAuthBearer;

        RegionEndpoint region = RegionEndpoint.GetBySystemName(
            configuration["AWS:Region"] ?? throw new InvalidOperationException("Configuration 'AWS:Region' not found."));

        return new ProducerBuilder<Null, string>(config)
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
