using ErrorOr;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Notifications;

public sealed class NotificationDispatchProcessorTests
{
    private static readonly RetryContext Context = new(RetryTopicChain.OriginalTopic);

    private const string ValidMessage = """
        {"correlationId":"11111111-1111-1111-1111-111111111111","recipientId":"22222222-2222-2222-2222-222222222222","recipientEmail":"user@example.com","channel":"Email","templateId":"welcome-email","payload":{"name":"Alice"}}
        """;

    private static (
        NotificationDispatchProcessor Processor,
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> Handler,
        Mock<IFailureRouter> Router) CreateSut()
    {
        Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler = new();
        Mock<IFailureRouter> router = new();
        NotificationDispatchProcessor processor = new(handler.Object, router.Object, Mock.Of<ILogger<NotificationDispatchProcessor>>());

        return (processor, handler, router);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotRoute_WhenHandlerSucceeds()
    {
        (NotificationDispatchProcessor sut, Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler, Mock<IFailureRouter> router) = CreateSut();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DispatchNotificationResponse(NotificationStatus.Sent, WasDuplicate: false));

        await sut.ProcessAsync(ValidMessage, Context);

        router.Verify(r => r.RouteAsync(
            It.IsAny<string>(), It.IsAny<RetryContext>(), It.IsAny<FailureClassification>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRouteToDlq_WhenMessageIsMalformedJson()
    {
        (NotificationDispatchProcessor sut, Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler, Mock<IFailureRouter> router) = CreateSut();

        await sut.ProcessAsync("{not-valid-json", Context);

        handler.Verify(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        router.Verify(r => r.RouteAsync(
            "{not-valid-json", Context, FailureClassification.PoisonPill,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRouteAsPoisonPill_WhenHandlerReturnsAPoisonPillError()
    {
        (NotificationDispatchProcessor sut, Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler, Mock<IFailureRouter> router) = CreateSut();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Error> { Error.Failure(TemplateErrorCodes.NotFound, "not found") });

        await sut.ProcessAsync(ValidMessage, Context);

        router.Verify(r => r.RouteAsync(
            ValidMessage, Context, FailureClassification.PoisonPill,
            TemplateErrorCodes.NotFound, "not found", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRouteAsTransient_WhenHandlerReturnsATransientError()
    {
        (NotificationDispatchProcessor sut, Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler, Mock<IFailureRouter> router) = CreateSut();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Error> { Error.Failure(SesErrorCodes.SendFailed, "ses down") });

        await sut.ProcessAsync(ValidMessage, Context);

        router.Verify(r => r.RouteAsync(
            ValidMessage, Context, FailureClassification.Transient,
            SesErrorCodes.SendFailed, "ses down", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldClassifyAndRoute_WhenHandlerThrows()
    {
        (NotificationDispatchProcessor sut, Mock<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>> handler, Mock<IFailureRouter> router) = CreateSut();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<DispatchNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unreachable"));

        await sut.ProcessAsync(ValidMessage, Context);

        router.Verify(r => r.RouteAsync(
            ValidMessage, Context, FailureClassification.Transient,
            nameof(InvalidOperationException), "db unreachable", It.IsAny<CancellationToken>()), Times.Once);
    }
}
