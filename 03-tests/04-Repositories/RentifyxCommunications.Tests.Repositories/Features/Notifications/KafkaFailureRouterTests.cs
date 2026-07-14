using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Messaging;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class KafkaFailureRouterTests
{
    private static string HeaderValue(Headers headers, string key) =>
        Encoding.UTF8.GetString(headers.GetLastBytes(key));

    private static (KafkaFailureRouter Router, Mock<IProducer<Null, string>> Producer) CreateSut()
    {
        Mock<IProducer<Null, string>> producer = new();
        producer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<Null, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<Null, string>());

        Mock<IKafkaProducerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(producer.Object);

        return (new KafkaFailureRouter(factory.Object), producer);
    }

    [Fact]
    public async Task RouteAsync_ShouldPublishToDlq_WhenClassificationIsPoisonPill()
    {
        (KafkaFailureRouter sut, Mock<IProducer<Null, string>> producer) = CreateSut();
        RetryContext context = new(RetryTopicChain.OriginalTopic);

        await sut.RouteAsync("payload", context, FailureClassification.PoisonPill, "JsonException", "malformed");

        producer.Verify(p => p.ProduceAsync(
            RetryTopicChain.DlqTopic,
            It.Is<Message<Null, string>>(m => m.Value == "payload"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RouteAsync_ShouldNotSetNextRetryAtHeader_WhenPublishingToDlq()
    {
        (KafkaFailureRouter sut, Mock<IProducer<Null, string>> producer) = CreateSut();
        RetryContext context = new(RetryTopicChain.OriginalTopic);
        Message<Null, string>? captured = null;
        producer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<Null, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<Null, string>, CancellationToken>((_, message, _) => captured = message)
            .ReturnsAsync(new DeliveryResult<Null, string>());

        await sut.RouteAsync("payload", context, FailureClassification.PoisonPill, "JsonException", "malformed");

        captured.Should().NotBeNull();
        captured!.Headers.TryGetLastBytes("x-next-retry-at", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RouteAsync_ShouldPublishToRetry5s_WhenTransientFromOriginalTopic()
    {
        (KafkaFailureRouter sut, Mock<IProducer<Null, string>> producer) = CreateSut();
        RetryContext context = new(RetryTopicChain.OriginalTopic, RetryCount: 0);

        await sut.RouteAsync("payload", context, FailureClassification.Transient, "InvalidOperationException", "db unreachable");

        producer.Verify(p => p.ProduceAsync(
            RetryTopicChain.Retry5sTopic,
            It.IsAny<Message<Null, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RouteAsync_ShouldPublishToDlq_WhenTransientAndRetriesExhausted()
    {
        (KafkaFailureRouter sut, Mock<IProducer<Null, string>> producer) = CreateSut();
        RetryContext context = new(RetryTopicChain.OriginalTopic, RetryCount: 3);

        await sut.RouteAsync("payload", context, FailureClassification.Transient, "InvalidOperationException", "db unreachable");

        producer.Verify(p => p.ProduceAsync(
            RetryTopicChain.DlqTopic,
            It.IsAny<Message<Null, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RouteAsync_ShouldSetAllRequiredHeaders_WhenTransient()
    {
        (KafkaFailureRouter sut, Mock<IProducer<Null, string>> producer) = CreateSut();
        RetryContext context = new(RetryTopicChain.OriginalTopic, RetryCount: 0);
        Message<Null, string>? captured = null;
        producer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<Null, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<Null, string>, CancellationToken>((_, message, _) => captured = message)
            .ReturnsAsync(new DeliveryResult<Null, string>());

        await sut.RouteAsync("payload", context, FailureClassification.Transient, "InvalidOperationException", "db unreachable");

        captured.Should().NotBeNull();
        HeaderValue(captured!.Headers, "x-original-topic").Should().Be(RetryTopicChain.OriginalTopic);
        HeaderValue(captured.Headers, "x-retry-count").Should().Be("1");
        HeaderValue(captured.Headers, "x-exception-type").Should().Be("InvalidOperationException");
        HeaderValue(captured.Headers, "x-exception-message").Should().Be("db unreachable");
        captured.Headers.TryGetLastBytes("x-first-failure-timestamp", out _).Should().BeTrue();
        captured.Headers.TryGetLastBytes("x-next-retry-at", out _).Should().BeTrue();
    }
}
