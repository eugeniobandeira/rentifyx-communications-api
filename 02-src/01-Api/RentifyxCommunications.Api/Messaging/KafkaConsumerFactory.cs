using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace RentifyxCommunications.Api.Messaging;

internal sealed class KafkaConsumerFactory(IConfiguration configuration) : IKafkaConsumerFactory
{
    public IConsumer<Ignore, string> Create()
    {
        string bootstrapServers = configuration.GetConnectionString("kafka")
            ?? throw new InvalidOperationException("Connection string 'kafka' not found.");

        ConsumerConfig config = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = configuration["Kafka:ConsumerGroupId"] ?? "rentifyx-communications-api",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        return new ConsumerBuilder<Ignore, string>(config).Build();
    }
}
