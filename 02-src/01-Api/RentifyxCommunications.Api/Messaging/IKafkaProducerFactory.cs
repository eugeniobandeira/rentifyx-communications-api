using Confluent.Kafka;

namespace RentifyxCommunications.Api.Messaging;

public interface IKafkaProducerFactory
{
    IProducer<Null, string> Create();
}
