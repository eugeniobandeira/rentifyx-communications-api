using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;

namespace RentifyxCommunications.Api.Messaging;

internal sealed class KafkaConsumerFactory(
    IConfiguration configuration,
    IOptions<KafkaOptions> kafkaOptions) : IKafkaConsumerFactory
{
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

        return new ConsumerBuilder<Ignore, string>(config).Build();
    }
}
