using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RentifyxCommunications.Api.Messaging;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Common;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Messaging;

public sealed class RetryTopicConsumerTests
{
    private static readonly NotificationMetrics Metrics = new();

    private const string ValidMessage = """
        {"correlationId":"11111111-1111-1111-1111-111111111111","recipientId":"22222222-2222-2222-2222-222222222222","recipientEmail":"user@example.com","channel":"Email","templateId":"welcome-email","payload":{"name":"Alice"}}
        """;

    [Fact]
    public async Task ProcessMessage_ShouldDelay_WhenNextRetryAtIsInTheFuture()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchNotificationResponse(NotificationStatus.Sent, WasDuplicate: false));

        Headers headers = BuildHeaders(nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(1));
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MessageResult(headers))
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        using RetryTopicConsumer sut = CreateSut(RetryTopicChain.Retry5sTopic, factory.Object, handler.Object);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => handler.Invocations.Count > 0, TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Generous lower bound (half the configured delay) to stay robust under parallel test-run CPU contention,
        // while still proving a real delay happened rather than immediate processing.
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task ProcessMessage_ShouldProcessImmediately_WhenNextRetryAtHasElapsed()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchNotificationResponse(NotificationStatus.Sent, WasDuplicate: false));

        Headers headers = BuildHeaders(nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-5));
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MessageResult(headers))
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        using RetryTopicConsumer sut = CreateSut(RetryTopicChain.Retry5sTopic, factory.Object, handler.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => handler.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_ShouldBuildRetryContext_FromHeaders()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErrorOr.Error> { ErrorOr.Error.Failure(SesErrorCodes.SendFailed, "ses down") });

        Headers headers = BuildHeaders(
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-5),
            originalTopic: RetryTopicChain.OriginalTopic,
            retryCount: 2);
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MessageResult(headers))
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        Mock<IFailureRouter> router = new();
        using RetryTopicConsumer sut = CreateSut(RetryTopicChain.Retry10mTopic, factory.Object, handler.Object, router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => router.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(),
            It.Is<RetryContext>(c => c.OriginalTopic == RetryTopicChain.OriginalTopic && c.RetryCount == 2),
            FailureClassification.Transient,
            SesErrorCodes.SendFailed,
            "ses down",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Headers BuildHeaders(DateTimeOffset nextRetryAt, string originalTopic = RetryTopicChain.OriginalTopic, int retryCount = 1)
    {
        return new Headers
        {
            new Header("x-original-topic", Encoding.UTF8.GetBytes(originalTopic)),
            new Header("x-retry-count", Encoding.UTF8.GetBytes(retryCount.ToString())),
            new Header("x-first-failure-timestamp", Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))),
            new Header("x-next-retry-at", Encoding.UTF8.GetBytes(nextRetryAt.ToString("O")))
        };
    }

    private static ConsumeResult<Ignore, string> MessageResult(Headers headers)
    {
        return new ConsumeResult<Ignore, string>
        {
            Message = new Message<Ignore, string> { Value = ValidMessage, Headers = headers },
            TopicPartitionOffset = new TopicPartitionOffset(new TopicPartition(RetryTopicChain.Retry5sTopic, new Partition(0)), new Offset(0))
        };
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeoutOverride = null)
    {
        using CancellationTokenSource timeout = new(timeoutOverride ?? TimeSpan.FromSeconds(5));
        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
        }
    }

    private static RetryTopicConsumer CreateSut(
        string topic,
        IKafkaConsumerFactory factory,
        IHandler<DispatchNotificationRequest, DispatchNotificationResponse> handler,
        IFailureRouter? router = null)
    {
        ServiceCollection services = new();
        services.AddSingleton(handler);
        services.AddSingleton(router ?? Mock.Of<IFailureRouter>());
        services.AddSingleton(Metrics);
        services.AddSingleton(Mock.Of<ILogger<NotificationDispatchProcessor>>());
        services.AddScoped<NotificationDispatchProcessor>();
        ServiceProvider provider = services.BuildServiceProvider();

        return new RetryTopicConsumer(
            topic,
            Mock.Of<ILogger<RetryTopicConsumer>>(),
            factory,
            provider.GetRequiredService<IServiceScopeFactory>());
    }
}
