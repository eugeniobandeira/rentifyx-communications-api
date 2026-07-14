using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace RentifyxCommunications.Api.Messaging;

internal sealed class KafkaProducerFactory(IConfiguration configuration) : IKafkaProducerFactory
{
    public IProducer<Null, string> Create()
    {
        string bootstrapServers = configuration.GetConnectionString("kafka")
            ?? throw new InvalidOperationException("Connection string 'kafka' not found.");

        ProducerConfig config = new()
        {
            BootstrapServers = bootstrapServers
        };

        return new ProducerBuilder<Null, string>(config).Build();
    }
}
