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
using RentifyxCommunications.Domain.ValueObjects;
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

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(request);

        result.IsError.Should().BeTrue();
        _notificationRepository.Verify(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _consentRepository.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidEmailFormat_ShouldReturnValidationError()
    {
        DispatchNotificationHandler sut = CreateSut();
        DispatchNotificationRequest request = ValidRequest() with { RecipientEmail = "not-an-email" };

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(request);

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

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.WasDuplicate.Should().BeTrue();
        result.Value.Status.Should().Be(NotificationStatus.Pending);
        _consentRepository.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveIfNotExistsReturnsTrue_ShouldProceedToConsentResolution()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Render.NotReached"));

        DispatchNotificationHandler sut = CreateSut();

        await sut.HandleAsync(ValidRequest());

        _consentRepository.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenConsentRecordOptedOut_ShouldSuppressWithoutRenderOrSend()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ConsentPreference optedOut = ConsentPreference.Create(Guid.NewGuid(), Channel.Email, optedIn: false, DateTime.UtcNow).Value;
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(optedOut);

        DispatchNotificationHandler sut = CreateSut();

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(NotificationStatus.Suppressed);
        _notificationRepository.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Suppressed, It.IsAny<CancellationToken>()), Times.Once);
        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConsentRecordOptedIn_ShouldProceedToRender()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ConsentPreference optedIn = ConsentPreference.Create(Guid.NewGuid(), Channel.Email, optedIn: true, DateTime.UtcNow).Value;
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(optedIn);
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Render.NotReached"));

        DispatchNotificationHandler sut = CreateSut();

        await sut.HandleAsync(ValidRequest());

        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNoConsentRecord_ShouldProceedToRender()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Render.NotReached"));

        DispatchNotificationHandler sut = CreateSut();

        await sut.HandleAsync(ValidRequest());

        _templateRenderer.Verify(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupHappyPathUpToConsent()
    {
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);
    }

    [Fact]
    public async Task Handle_WhenRenderFails_ShouldMarkFailedWithoutSending()
    {
        SetupHappyPathUpToConsent();
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Render.TemplateNotFound"));

        DispatchNotificationHandler sut = CreateSut();

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(NotificationStatus.Failed);
        _notificationRepository.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Failed, It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSendSucceeds_ShouldMarkSentAndPersistDispatchingBeforeSend()
    {
        SetupHappyPathUpToConsent();
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rendered content");
        _emailSender
            .Setup(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        List<string> callOrder = [];
        _notificationRepository
            .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Dispatching, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("UpdateStatusAsync(Dispatching)"))
            .Returns(Task.CompletedTask);
        _emailSender
            .Setup(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("SendAsync"))
            .ReturnsAsync(Result.Success);

        DispatchNotificationHandler sut = CreateSut();

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(NotificationStatus.Sent);
        callOrder.Should().Equal("UpdateStatusAsync(Dispatching)", "SendAsync");
        _notificationRepository.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Sent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSendFails_ShouldMarkFailed()
    {
        SetupHappyPathUpToConsent();
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rendered content");
        _emailSender
            .Setup(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Ses.Throttled"));

        DispatchNotificationHandler sut = CreateSut();

        ErrorOr<DispatchOutcome> result = await sut.HandleAsync(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(NotificationStatus.Failed);
        _notificationRepository.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Failed, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HappyPath_ShouldCallCollaboratorsInExpectedOrder()
    {
        List<string> callOrder = [];
        _notificationRepository
            .Setup(r => r.SaveIfNotExistsAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("SaveIfNotExists"))
            .ReturnsAsync(true);
        _consentRepository
            .Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("FindAsync"))
            .ReturnsAsync((ConsentPreference?)null);
        _templateRenderer
            .Setup(r => r.RenderAsync(It.IsAny<TemplateId>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("RenderAsync"))
            .ReturnsAsync("rendered content");
        _notificationRepository
            .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Dispatching, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("UpdateStatusAsync(Dispatching)"))
            .Returns(Task.CompletedTask);
        _emailSender
            .Setup(s => s.SendAsync(It.IsAny<EmailAddress>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("SendAsync"))
            .ReturnsAsync(Result.Success);
        _notificationRepository
            .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), NotificationStatus.Sent, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("UpdateStatusAsync(Sent)"))
            .Returns(Task.CompletedTask);

        DispatchNotificationHandler sut = CreateSut();

        await sut.HandleAsync(ValidRequest());

        callOrder.Should().Equal(
            "SaveIfNotExists",
            "FindAsync",
            "RenderAsync",
            "UpdateStatusAsync(Dispatching)",
            "SendAsync",
            "UpdateStatusAsync(Sent)");
    }
}
