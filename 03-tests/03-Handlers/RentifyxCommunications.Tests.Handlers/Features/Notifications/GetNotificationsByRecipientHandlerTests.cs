using ErrorOr;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Response;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Validator;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Notifications;

public sealed class GetNotificationsByRecipientHandlerTests
{
    private readonly Mock<INotificationRepository> _notificationRepository = new();

    private GetNotificationsByRecipientHandler CreateSut()
    {
        return new GetNotificationsByRecipientHandler(
            new GetNotificationsByRecipientValidator(),
            _notificationRepository.Object);
    }

    private static NotificationEntity CreateNotification(Guid recipientId)
    {
        EmailAddress email = EmailAddress.Create("user@example.com").Value;
        TemplateId templateId = TemplateId.Create("welcome-email").Value;

        return NotificationEntity.Create(
            Guid.NewGuid(),
            recipientId,
            email,
            Channel.Email,
            templateId,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;
    }

    [Fact]
    public async Task Handle_WhenRecipientHasNotifications_ShouldReturnMappedList()
    {
        Guid recipientId = Guid.NewGuid();
        NotificationEntity notification = CreateNotification(recipientId);
        _notificationRepository
            .Setup(r => r.GetByRecipientAsync(recipientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([notification]);

        GetNotificationsByRecipientHandler sut = CreateSut();

        ErrorOr<NotificationListResponse> result = await sut.HandleAsync(new GetNotificationsByRecipientRequest(recipientId.ToString()));

        result.IsError.Should().BeFalse();
        result.Value.Notifications.Should().ContainSingle();
        NotificationListItem item = result.Value.Notifications[0];
        item.Id.Should().Be(notification.Id);
        item.Channel.Should().Be(notification.Channel);
        item.Status.Should().Be(notification.Status);
        item.CreatedAt.Should().Be(notification.CreatedAt);
    }

    [Fact]
    public async Task Handle_WhenRecipientHasNoNotifications_ShouldReturnEmptyListNotError()
    {
        Guid recipientId = Guid.NewGuid();
        _notificationRepository
            .Setup(r => r.GetByRecipientAsync(recipientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        GetNotificationsByRecipientHandler sut = CreateSut();

        ErrorOr<NotificationListResponse> result = await sut.HandleAsync(new GetNotificationsByRecipientRequest(recipientId.ToString()));

        result.IsError.Should().BeFalse();
        result.Value.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithMalformedRecipientId_ShouldReturnValidationErrorWithoutTouchingRepository()
    {
        GetNotificationsByRecipientHandler sut = CreateSut();

        ErrorOr<NotificationListResponse> result = await sut.HandleAsync(new GetNotificationsByRecipientRequest("not-a-guid"));

        result.IsError.Should().BeTrue();
        _notificationRepository.Verify(
            r => r.GetByRecipientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
