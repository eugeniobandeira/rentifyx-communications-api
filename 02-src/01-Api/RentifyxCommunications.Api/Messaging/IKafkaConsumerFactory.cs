using Confluent.Kafka;

namespace RentifyxCommunications.Api.Messaging;

public interface IKafkaConsumerFactory
{
    IConsumer<Ignore, string> Create();
}
