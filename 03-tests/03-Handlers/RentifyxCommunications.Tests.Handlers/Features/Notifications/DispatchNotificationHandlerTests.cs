using ErrorOr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Validator;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Notifications;

public sealed class DispatchNotificationHandlerTests
{
    private readonly Mock<INotificationRepository> _notificationRepository = new();
    private readonly Mock<IConsentRepository> _consentRepository = new();
    private readonly Mock<ITemplateRenderer> _templateRenderer = new();
    private readonly Mock<IEmailSender> _emailSender = new();

    private static DispatchNotificationRequest ValidRequest()
    {
        return new DispatchNotificationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user@example.com",
            "Email",
            "welcome-email",
            new Dictionary<string, string> { ["name"] = "Alice" });
    }

    private DispatchNotificationHandler CreateSut()
    {
        return new DispatchNotificationHandler(
            new DispatchNotificationValidator(),
            _notificationRepository.Object,
            _consentRepository.Object,
            _templateRenderer.Object,
            _emailSender.Object,
            NullLogger<DispatchNotificationHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldReturnValidationErrorsWithoutTouchingRepositories()
    {
        DispatchNotificationHandler sut = CreateSut();
        DispatchNotificationRequest request = ValidRequest() with { CorrelationId = Guid.Empty };

        ErrorOr<DispatchOutcome> result = await sut.Handle(request);

        result.IsError.Should().BeTrue();
        _notificationRepository.Verify(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _consentRepository.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<Domain.ValueObjects.TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<Domain.ValueObjects.EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidEmailFormat_ShouldReturnValidationError()
    {
        DispatchNotificationHandler sut = CreateSut();
        DispatchNotificationRequest request = ValidRequest() with { RecipientEmail = "not-an-email" };

        ErrorOr<DispatchOutcome> result = await sut.Handle(request);

        result.IsError.Should().BeTrue();
        _notificationRepository.Verify(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveIfNotExistsReturnsFalse_ShouldReturnDuplicateOutcomeWithoutFurtherCalls()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DispatchNotificationHandler sut = CreateSut();

        ErrorOr<DispatchOutcome> result = await sut.Handle(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.WasDuplicate.Should().BeTrue();
        result.Value.Status.Should().Be(NotificationStatus.Pending);
        _consentRepository.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<Domain.ValueObjects.TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<Domain.ValueObjects.EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveIfNotExistsReturnsTrue_ShouldProceedToConsentResolution()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.ValueObjects.ConsentPreference?)null);
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<Domain.ValueObjects.TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Render.NotReached"));

        DispatchNotificationHandler sut = CreateSut();

        await sut.Handle(ValidRequest());

        _consentRepository.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
