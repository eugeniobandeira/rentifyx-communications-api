using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RentifyxCommunications.Api.Messaging;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Messaging;

public sealed class DlqObserverHostedServiceTests
{
    private static readonly Guid CorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string ValidMessage(Guid correlationId) =>
        "{\"correlationId\":\"" + correlationId + "\",\"recipientId\":\"22222222-2222-2222-2222-222222222222\"," +
        "\"recipientEmail\":\"user@example.com\",\"channel\":\"Email\",\"templateId\":\"welcome-email\"," +
        "\"payload\":{\"name\":\"Alice\"}}";

    [Fact]
    public async Task ProcessMessage_ShouldMarkNotificationFailed_WhenRecordExists()
    {
        NotificationEntity notification = NotificationEntity.Create(
            CorrelationId,
            Guid.NewGuid(),
            EmailAddress.Create("user@example.com").Value,
            Channel.Email,
            TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;

        Mock<INotificationRepository> repository = new();
        repository.Setup(r => r.GetByCorrelationIdAsync(CorrelationId, It.IsAny<CancellationToken>())).ReturnsAsync(notification);

        Headers headers = new()
        {
            new Header("x-exception-message", Encoding.UTF8.GetBytes("ses down"))
        };
        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MessageResult(ValidMessage(CorrelationId), headers))
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        using DlqObserverHostedService sut = CreateSut(factory.Object, repository.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => repository.Invocations.Any(i => i.Method.Name == nameof(INotificationRepository.UpdateStatusAsync)));
        await sut.StopAsync(CancellationToken.None);

        repository.Verify(r => r.UpdateStatusAsync(notification.Id, NotificationStatus.Failed, "ses down", It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessMessage_ShouldSkipUpdate_WhenNoMatchingRecordExists()
    {
        Mock<INotificationRepository> repository = new();
        repository.Setup(r => r.GetByCorrelationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((NotificationEntity?)null);

        Mock<IConsumer<Ignore, string>> consumer = new();
        consumer.SetupSequence(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(MessageResult(ValidMessage(CorrelationId), []))
            .Returns((ConsumeResult<Ignore, string>)null!);

        Mock<IKafkaConsumerFactory> factory = new();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(consumer.Object);

        using DlqObserverHostedService sut = CreateSut(factory.Object, repository.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => repository.Invocations.Any(i => i.Method.Name == nameof(INotificationRepository.GetByCorrelationIdAsync)));
        await sut.StopAsync(CancellationToken.None);

        repository.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<NotificationStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<Ignore, string>>()), Times.AtLeastOnce);
    }

    private static ConsumeResult<Ignore, string> MessageResult(string value, Headers headers)
    {
        return new ConsumeResult<Ignore, string>
        {
            Message = new Message<Ignore, string> { Value = value, Headers = headers },
            TopicPartitionOffset = new TopicPartitionOffset(new TopicPartition(RetryTopicChain.DlqTopic, new Partition(0)), new Offset(0))
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

    private static DlqObserverHostedService CreateSut(IKafkaConsumerFactory factory, INotificationRepository repository)
    {
        ServiceCollection services = new();
        services.AddSingleton(repository);
        ServiceProvider provider = services.BuildServiceProvider();

        return new DlqObserverHostedService(
            Mock.Of<ILogger<DlqObserverHostedService>>(),
            factory,
            provider.GetRequiredService<IServiceScopeFactory>());
    }
}
