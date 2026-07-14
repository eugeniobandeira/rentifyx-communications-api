using System.Globalization;
using System.Text;
using Confluent.Kafka;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Messaging;

public sealed class KafkaFailureRouter(IKafkaProducerFactory producerFactory) : IFailureRouter
{
    private const string OriginalTopicHeader = "x-original-topic";
    private const string RetryCountHeader = "x-retry-count";
    private const string FirstFailureTimestampHeader = "x-first-failure-timestamp";
    private const string ExceptionTypeHeader = "x-exception-type";
    private const string ExceptionMessageHeader = "x-exception-message";
    private const string NextRetryAtHeader = "x-next-retry-at";

    public async Task RouteAsync(
        string rawMessage,
        RetryContext context,
        FailureClassification classification,
        string exceptionType,
        string exceptionMessage,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset firstFailureTimestamp = context.FirstFailureTimestamp ?? DateTimeOffset.UtcNow;

        string targetTopic = classification == FailureClassification.PoisonPill
            ? RetryTopicChain.DlqTopic
            : RetryTopicChain.NextStage(context.RetryCount);

        int retryCount = classification == FailureClassification.PoisonPill
            ? context.RetryCount
            : context.RetryCount + 1;

        Headers headers = new()
        {
            new Header(OriginalTopicHeader, Encoding.UTF8.GetBytes(context.OriginalTopic)),
            new Header(RetryCountHeader, Encoding.UTF8.GetBytes(retryCount.ToString(CultureInfo.InvariantCulture))),
            new Header(FirstFailureTimestampHeader, Encoding.UTF8.GetBytes(firstFailureTimestamp.ToString("O", CultureInfo.InvariantCulture))),
            new Header(ExceptionTypeHeader, Encoding.UTF8.GetBytes(exceptionType)),
            new Header(ExceptionMessageHeader, Encoding.UTF8.GetBytes(exceptionMessage))
        };

        if (targetTopic != RetryTopicChain.DlqTopic)
        {
            DateTimeOffset nextRetryAt = DateTimeOffset.UtcNow + RetryTopicChain.DelayFor(targetTopic);
            headers.Add(new Header(NextRetryAtHeader, Encoding.UTF8.GetBytes(nextRetryAt.ToString("O", CultureInfo.InvariantCulture))));
        }

        using IProducer<Null, string> producer = producerFactory.Create();
        await producer.ProduceAsync(
            targetTopic,
            new Message<Null, string> { Value = rawMessage, Headers = headers },
            cancellationToken);
    }
}
