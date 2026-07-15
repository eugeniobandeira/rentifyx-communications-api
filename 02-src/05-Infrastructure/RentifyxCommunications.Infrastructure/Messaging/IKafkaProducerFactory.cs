using Confluent.Kafka;

namespace RentifyxCommunications.Infrastructure.Messaging;

public interface IKafkaProducerFactory
{
    IProducer<Null, string> Create();
}
