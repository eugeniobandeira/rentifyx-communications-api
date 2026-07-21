using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RentifyxCommunications.Api.Messaging;
using RentifyxCommunications.Application.Abstractions;
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

public sealed class NotificationRequestedConsumerTests
{
    private static readonly NotificationMetrics Metrics = new();

    [Fact]
    public async Task StartAsync_LogsSubscription_WhenConsumerFactorySucceeds()
    {
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        SharedLogEntries entries = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        entries.Should().Contain(e =>
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
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        using NotificationRequestedConsumer sut = CreateSut(new SharedLogEntries(), factory.Object);
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
        factory.Setup(f => f.Create(It.IsAny<string>())).Throws(new KafkaException(ErrorCode.Local_Transport));

        SharedLogEntries entries = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object, retryDelayOverride: TimeSpan.Zero);

        Func<Task> act = () => sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        entries.Count(e => e.Level == LogLevel.Error).Should().Be(NotificationRequestedConsumer.MaxStartupAttempts + 1);
        factory.Verify(f => f.Create(It.IsAny<string>()), Times.Exactly(NotificationRequestedConsumer.MaxStartupAttempts));
    }

    [Fact]
    public async Task ConsumeLoop_WithValidMessage_ShouldCallHandlerAndLogInformation()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchNotificationResponse(NotificationStatus.Sent, WasDuplicate: false));

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        SharedLogEntries entries = new();
        Mock<IFailureRouter> router = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object, handler.Object, router: router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("Notification processed", StringComparison.Ordinal)));
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(), It.IsAny<RetryContext>(), It.IsAny<FailureClassification>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeLoop_WithMalformedJson_ShouldRouteToDlqAndCommit()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MalformedMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        SharedLogEntries entries = new();
        Mock<IFailureRouter> router = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object, handler.Object, router: router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Malformed", StringComparison.Ordinal)));
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(), It.IsAny<RetryContext>(), FailureClassification.PoisonPill,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeLoop_WithTransientHandlerFailure_ShouldRouteAsTransientAndCommit()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErrorOr.Error> { ErrorOr.Error.Failure(SesErrorCodes.SendFailed, "ses down") });

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        SharedLogEntries entries = new();
        Mock<IFailureRouter> router = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object, handler.Object, router: router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => router.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(), It.IsAny<RetryContext>(), FailureClassification.Transient,
            SesErrorCodes.SendFailed, "ses down", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeLoop_WhenHandlerThrows_ShouldLogErrorAndCommitWithoutCrashingLoop()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated infra failure"));

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        SharedLogEntries entries = new();
        Mock<IFailureRouter> router = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object, handler.Object, router: router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => router.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(), It.IsAny<RetryContext>(), FailureClassification.Transient,
            nameof(InvalidOperationException), "simulated infra failure", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeLoop_WithMalformedMessageFollowedByValidMessage_ShouldStillProcessValidMessage()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchNotificationResponse(NotificationStatus.Sent, WasDuplicate: false));

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MalformedMessageResult())
            .Returns(ValidMessageResult())
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        SharedLogEntries entries = new();
        using NotificationRequestedConsumer sut = CreateSut(entries, factory.Object, handler.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => handler.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        handler.Verify(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
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
        SharedLogEntries entries,
        IKafkaConsumerFactory factory,
        IHandler<DispatchNotificationRequest, DispatchNotificationResponse>? handler = null,
        TimeSpan? retryDelayOverride = null,
        IFailureRouter? router = null)
    {
        ServiceCollection services = new();
        services.AddSingleton(handler ?? Mock.Of<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>>());
        services.AddSingleton(router ?? Mock.Of<IFailureRouter>());
        services.AddSingleton(Metrics);
        services.AddSingleton<ILogger<NotificationDispatchProcessor>>(new ListLogger<NotificationDispatchProcessor>(entries));
        services.AddScoped<NotificationDispatchProcessor>();
        ServiceProvider provider = services.BuildServiceProvider();

        return new NotificationRequestedConsumer(
            new ListLogger<NotificationRequestedConsumer>(entries),
            factory,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions("test-consumer-group")),
            startupRetryDelayOverride: retryDelayOverride ?? TimeSpan.Zero);
    }

    private sealed class SharedLogEntries : List<(LogLevel Level, string Message)>;

    private sealed class ListLogger<T>(SharedLogEntries entries) : ILogger<T>
    {
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
            lock (entries)
            {
                entries.Add((logLevel, formatter(state, exception)));
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
