using Confluent.Kafka;
using ErrorOr;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RentifyxCommunications.Api.Consumers;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Domain.Enums;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Consumers;

public sealed class NotificationRequestedConsumerTests
{
    [Fact]
    public async Task StartAsync_LogsSubscription_WhenConsumerFactorySucceeds()
    {
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        ListLogger logger = new();
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains(NotificationRequestedConsumer.Topic, StringComparison.Ordinal));
        consumer.Verify(c => c.Subscribe(NotificationRequestedConsumer.Topic), Times.Once);
    }

    [Fact]
    public async Task StopAsync_CancelsConsumeLoopAndClosesConsumer_WithoutHanging()
    {
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        using NotificationRequestedConsumer sut = CreateSut(new ListLogger(), factory.Object);
        await sut.StartAsync(CancellationToken.None);

        Task stopTask = sut.StopAsync(CancellationToken.None);
        Task completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1)));

        completed.Should().Be(stopTask);
        consumer.Verify(c => c.Close(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenConsumerFactoryAlwaysFails()
    {
        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Throws(new KafkaException(ErrorCode.Local_Transport));

        ListLogger logger = new();
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object, retryDelayOverride: TimeSpan.Zero);

        Func<Task> act = () => sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Entries.Count(e => e.Level == LogLevel.Error).Should().Be(NotificationRequestedConsumer.MaxStartupAttempts + 1);
        factory.Verify(f => f.Create(), Times.Exactly(NotificationRequestedConsumer.MaxStartupAttempts));
    }

    [Fact]
    public async Task ConsumeLoop_WithValidMessage_ShouldCallHandlerAndLogInformation()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchOutcome>> handler = new();
        handler
            .Setup(h => h.Handle(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchOutcome(NotificationStatus.Sent, WasDuplicate: false));

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        ListLogger logger = new();
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object, handler.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("Notification processed", StringComparison.Ordinal)));
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.Handle(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConsumeLoop_WithMalformedJson_ShouldLogErrorAndCommit()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchOutcome>> handler = new();

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MalformedMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        ListLogger logger = new();
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object, handler.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Malformed", StringComparison.Ordinal)));
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.Handle(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConsumeLoop_WhenHandlerThrows_ShouldLogErrorAndCommitWithoutCrashingLoop()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchOutcome>> handler = new();
        handler
            .Setup(h => h.Handle(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated infra failure"));

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        ListLogger logger = new();
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object, handler.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Unexpected error", StringComparison.Ordinal)));
        await sut.StopAsync(CancellationToken.None);

        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConsumeLoop_WithMalformedMessageFollowedByValidMessage_ShouldStillProcessValidMessage()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchOutcome>> handler = new();
        handler
            .Setup(h => h.Handle(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchOutcome(NotificationStatus.Sent, WasDuplicate: false));

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MalformedMessageResult())
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create()).Returns(consumer.Object);

        ListLogger logger = new();
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object, handler.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => handler.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.Handle(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ConsumeResult<Ignore, string> ValidMessageResult()
    {
        string json = """
            {"correlationId":"11111111-1111-1111-1111-111111111111","recipientId":"22222222-2222-2222-2222-222222222222","recipientEmail":"user@example.com","channel":"Email","templateId":"welcome-email","payload":{"name":"Alice"}}
            """;

        return new ConsumeResult<Ignore, string>
        {
            Message = new Message<Ignore, string> { Value = json },
            TopicPartitionOffset = new TopicPartitionOffset(new TopicPartition(NotificationRequestedConsumer.Topic, new Partition(0)), new Offset(0))
        };
    }

    private static ConsumeResult<Ignore, string> MalformedMessageResult()
    {
        return new ConsumeResult<Ignore, string>
        {
            Message = new Message<Ignore, string> { Value = "{not-valid-json" },
            TopicPartitionOffset = new TopicPartitionOffset(new TopicPartition(NotificationRequestedConsumer.Topic, new Partition(0)), new Offset(1))
        };
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
        }
    }

    private static NotificationRequestedConsumer CreateSut(
        ILogger<NotificationRequestedConsumer> logger,
        IKafkaConsumerFactory factory,
        IHandler<DispatchNotificationRequest, DispatchOutcome>? handler = null,
        TimeSpan? retryDelayOverride = null)
    {
        ServiceCollection services = new();
        services.AddSingleton(handler ?? Mock.Of<IHandler<DispatchNotificationRequest, DispatchOutcome>>());
        ServiceProvider provider = services.BuildServiceProvider();

        IConfiguration configuration = new ConfigurationBuilder().Build();
        return new NotificationRequestedConsumer(
            logger,
            factory,
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            retryDelayOverride ?? TimeSpan.Zero);
    }

    private sealed class ListLogger : ILogger<NotificationRequestedConsumer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
