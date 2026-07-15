using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RentifyxCommunications.Api.Messaging;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Options;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Messaging;

public sealed class ReconciliationHostedServiceTests
{
    private static NotificationEntity StuckNotification()
    {
        return NotificationEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("user@example.com").Value,
            Channel.Email,
            TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;
    }

    [Fact]
    public async Task Reconcile_ShouldRouteStuckNotification_ToRetry5sTopic()
    {
        NotificationEntity stuck = StuckNotification();
        Mock<INotificationRepository> repository = new();
        repository
            .Setup(r => r.GetStuckDispatchingAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([stuck]);

        Mock<IFailureRouter> router = new();

        using ReconciliationHostedService sut = CreateSut(repository.Object, router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => router.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(),
            It.Is<RetryContext>(c => c.OriginalTopic == RetryTopicChain.OriginalTopic && c.RetryCount == 0),
            FailureClassification.Transient,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Reconcile_ShouldNotRoute_WhenNoStuckNotificationsExist()
    {
        Mock<INotificationRepository> repository = new();
        repository
            .Setup(r => r.GetStuckDispatchingAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        Mock<IFailureRouter> router = new();

        using ReconciliationHostedService sut = CreateSut(repository.Object, router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => repository.Invocations.Count > 0);
        await sut.StopAsync(CancellationToken.None);

        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(), It.IsAny<RetryContext>(), It.IsAny<FailureClassification>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_ShouldKeepPolling_WhenRouteAsyncThrowsForOneRecord()
    {
        NotificationEntity stuck = StuckNotification();
        Mock<INotificationRepository> repository = new();
        repository
            .Setup(r => r.GetStuckDispatchingAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([stuck]);

        Mock<IFailureRouter> router = new();
        router
            .Setup(r => r.RouteAsync(
                It.IsAny<string>(), It.IsAny<RetryContext>(), It.IsAny<FailureClassification>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kafka unavailable"));

        using ReconciliationHostedService sut = CreateSut(repository.Object, router.Object);

        await sut.StartAsync(CancellationToken.None);
        await WaitForAsync(() => router.Invocations.Count >= 2, TimeSpan.FromSeconds(6));
        await sut.StopAsync(CancellationToken.None);

        router.Invocations.Count.Should().BeGreaterThanOrEqualTo(2, "a failure on one tick must not stop the next tick from running");
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeoutOverride = null)
    {
        using CancellationTokenSource timeout = new(timeoutOverride ?? TimeSpan.FromSeconds(5));
        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(20);
        }
    }

    private static ReconciliationHostedService CreateSut(INotificationRepository repository, IFailureRouter router)
    {
        ServiceCollection services = new();
        services.AddSingleton(repository);
        services.AddSingleton(router);
        ServiceProvider provider = services.BuildServiceProvider();

        ReconciliationOptions options = new(PollIntervalSeconds: 1, StalenessThresholdSeconds: 120);

        return new ReconciliationHostedService(
            Mock.Of<ILogger<ReconciliationHostedService>>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            options);
    }
}
