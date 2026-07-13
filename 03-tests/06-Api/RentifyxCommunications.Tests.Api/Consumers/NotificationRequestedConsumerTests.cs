using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RentifyxCommunications.Api.Consumers;
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
        using NotificationRequestedConsumer sut = CreateSut(logger, factory.Object, TimeSpan.Zero);

        Func<Task> act = () => sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Entries.Count(e => e.Level == LogLevel.Error).Should().Be(NotificationRequestedConsumer.MaxStartupAttempts + 1);
        factory.Verify(f => f.Create(), Times.Exactly(NotificationRequestedConsumer.MaxStartupAttempts));
    }

    private static NotificationRequestedConsumer CreateSut(
        ILogger<NotificationRequestedConsumer> logger,
        IKafkaConsumerFactory factory,
        TimeSpan? retryDelayOverride = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        return new NotificationRequestedConsumer(logger, factory, configuration, retryDelayOverride ?? TimeSpan.Zero);
    }

    private sealed class ListLogger : ILogger<NotificationRequestedConsumer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
