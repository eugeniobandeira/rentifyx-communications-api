using Confluent.Kafka;

namespace RentifyxCommunications.Api.Messaging;

public interface IKafkaConsumerFactory
{
    /// <summary>
    /// Builds a consumer with group ID "{KafkaOptions.ConsumerGroupId}-{groupIdSuffix}".
    /// Each caller (NotificationRequestedConsumer, RetryTopicConsumer per retry topic,
    /// DlqObserverHostedService) needs its own distinct consumer group - they subscribe
    /// to different topics, and sharing one group ID across heterogeneous subscriptions
    /// causes Kafka's range assignor to leave some topic-partitions unassigned to any
    /// member that actually subscribed to that topic.
    /// </summary>
    IConsumer<Ignore, string> Create(string groupIdSuffix);
}
