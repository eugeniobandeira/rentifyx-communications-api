namespace RentifyxCommunications.Api.Messaging;

/// <summary>
/// Shared timing constants for the Kafka-consuming <see cref="IHostedService"/>s
/// (<see cref="NotificationRequestedConsumer"/>, <see cref="RetryTopicConsumer"/>,
/// <see cref="DlqObserverHostedService"/>, <see cref="ReconciliationHostedService"/>)
/// so a tuning change is made once, not independently in each file.
/// </summary>
internal static class KafkaConsumerHostedServiceDefaults
{
    internal static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ConsumePollTimeout = TimeSpan.FromSeconds(1);
}
