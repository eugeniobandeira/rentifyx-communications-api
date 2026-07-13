using Confluent.Kafka;

namespace RentifyxCommunications.Api.Consumers;

public interface IKafkaConsumerFactory
{
    IConsumer<Ignore, string> Create();
}
