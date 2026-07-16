using ErrorOr;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Response;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Validator;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Notifications;

public sealed class GetNotificationStatusHandlerTests
{
    private readonly Mock<INotificationRepository> _notificationRepository = new();

    private GetNotificationStatusHandler CreateSut()
    {
        return new GetNotificationStatusHandler(
            new GetNotificationStatusValidator(),
            _notificationRepository.Object);
    }

    private static NotificationEntity CreateNotification(
        Guid id,
        NotificationStatus status = NotificationStatus.Sent,
        string? failureReason = null)
    {
        EmailAddress recipient = EmailAddress.Create("user@example.com").Value;
        TemplateId templateId = TemplateId.Create("welcome-email").Value;

        return NotificationEntity.Rehydrate(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            recipient,
            Channel.Email,
            templateId,
            new Dictionary<string, string> { ["name"] = "Alice" },
            status,
            failureReason,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidIdAndExistingNotification_ShouldReturnSuccessWithMappedResponse()
    {
        Guid id = Guid.NewGuid();
        NotificationEntity notification = CreateNotification(id);
        _notificationRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        GetNotificationStatusHandler sut = CreateSut();

        ErrorOr<NotificationStatusResponse> result = await sut.HandleAsync(new GetNotificationStatusRequest(id.ToString()));

        result.IsError.Should().BeFalse();
        result.Value.Id.Should().Be(notification.Id);
    }

    [Fact]
    public async Task Handle_WithValidIdAndMissingNotification_ShouldReturnNotFound()
    {
        Guid id = Guid.NewGuid();
        _notificationRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationEntity?)null);

        GetNotificationStatusHandler sut = CreateSut();

        ErrorOr<NotificationStatusResponse> result = await sut.HandleAsync(new GetNotificationStatusRequest(id.ToString()));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Validator_WithMalformedGuidString_ShouldFail()
    {
        GetNotificationStatusValidator validator = new();

        FluentValidation.Results.ValidationResult result = validator.Validate(new GetNotificationStatusRequest("not-a-guid"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMalformedGuidString_ShouldReturnValidationErrorWithoutCallingRepository()
    {
        GetNotificationStatusHandler sut = CreateSut();

        ErrorOr<NotificationStatusResponse> result = await sut.HandleAsync(new GetNotificationStatusRequest("not-a-guid"));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        _notificationRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithExistingNotification_ShouldMapAllResponseFieldsAccurately()
    {
        Guid id = Guid.NewGuid();
        NotificationEntity notification = CreateNotification(id, NotificationStatus.Failed, "Ses.Throttled");
        _notificationRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        GetNotificationStatusHandler sut = CreateSut();

        ErrorOr<NotificationStatusResponse> result = await sut.HandleAsync(new GetNotificationStatusRequest(id.ToString()));

        result.IsError.Should().BeFalse();
        NotificationStatusResponse response = result.Value;
        response.Id.Should().Be(notification.Id);
        response.CorrelationId.Should().Be(notification.CorrelationId);
        response.RecipientId.Should().Be(notification.RecipientId);
        response.Channel.Should().Be(notification.Channel);
        response.Status.Should().Be(notification.Status);
        response.FailureReason.Should().Be(notification.FailureReason);
        response.CreatedAt.Should().Be(notification.CreatedAt);
        response.UpdatedAt.Should().Be(notification.UpdatedAt);
    }
}
