using ErrorOr;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Response;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Validator;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Consent;

public sealed class UpdateConsentHandlerTests
{
    private readonly Mock<IConsentRepository> _consentRepository = new();
    private readonly Mock<IConsentAuditRepository> _consentAuditRepository = new();

    private static UpdateConsentRequest ValidRequest(bool optedIn = true)
    {
        return new UpdateConsentRequest(
            Guid.NewGuid().ToString(),
            "Email",
            optedIn);
    }

    private UpdateConsentHandler CreateSut()
    {
        return new UpdateConsentHandler(
            new UpdateConsentValidator(),
            _consentRepository.Object,
            _consentAuditRepository.Object);
    }

    [Fact]
    public async Task Handle_WhenNoPriorRecordExists_ShouldCreateConsentAndAuditWithNullPreviousOptedIn()
    {
        _consentRepository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);

        UpdateConsentRequest request = ValidRequest(optedIn: true);
        UpdateConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeFalse();
        result.Value.OptedIn.Should().BeTrue();
        _consentRepository.Verify(
            r => r.UpdateAsync(It.Is<ConsentPreference>(p => p.OptedIn), It.IsAny<CancellationToken>()),
            Times.Once);
        _consentAuditRepository.Verify(
            r => r.AddAsync(It.Is<ConsentAuditEntry>(e => e.PreviousOptedIn == null && e.NewOptedIn), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPriorRecordExists_ShouldOverwriteConsentAndAuditWithOldPreviousOptedIn()
    {
        Guid recipientId = Guid.NewGuid();
        ConsentPreference existing = ConsentPreference.Create(recipientId, Channel.Email, optedIn: false, DateTime.UtcNow).Value;
        _consentRepository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        UpdateConsentRequest request = new(recipientId.ToString(), "Email", true);
        UpdateConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeFalse();
        _consentRepository.Verify(
            r => r.UpdateAsync(It.Is<ConsentPreference>(p => p.OptedIn), It.IsAny<CancellationToken>()),
            Times.Once);
        _consentAuditRepository.Verify(
            r => r.AddAsync(It.Is<ConsentAuditEntry>(e => e.PreviousOptedIn == false && e.NewOptedIn), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithMalformedRecipientId_ShouldReturnValidationErrorWithoutTouchingRepositories()
    {
        UpdateConsentRequest request = new("not-a-guid", "Email", true);
        UpdateConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeTrue();
        _consentRepository.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
        _consentRepository.Verify(r => r.UpdateAsync(It.IsAny<ConsentPreference>(), It.IsAny<CancellationToken>()), Times.Never);
        _consentAuditRepository.Verify(r => r.AddAsync(It.IsAny<ConsentAuditEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidChannel_ShouldReturnValidationError()
    {
        UpdateConsentRequest request = new(Guid.NewGuid().ToString(), "NotAChannel", true);
        UpdateConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeTrue();
        _consentRepository.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAuditWriteThrowsAfterUpdateSucceeded_ShouldReturnErrorButKeepConsentWriteApplied()
    {
        _consentRepository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);
        _consentRepository
            .Setup(r => r.UpdateAsync(It.IsAny<ConsentPreference>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _consentAuditRepository
            .Setup(r => r.AddAsync(It.IsAny<ConsentAuditEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DynamoDB unavailable"));

        UpdateConsentRequest request = ValidRequest(optedIn: true);
        UpdateConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeTrue();
        _consentRepository.Verify(r => r.UpdateAsync(It.IsAny<ConsentPreference>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HappyPath_ShouldCallCollaboratorsInExpectedOrder()
    {
        _consentRepository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);

        List<string> callOrder = [];
        _consentRepository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("GetAsync"))
            .ReturnsAsync((ConsentPreference?)null);
        _consentRepository
            .Setup(r => r.UpdateAsync(It.IsAny<ConsentPreference>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("UpdateAsync"))
            .Returns(Task.CompletedTask);
        _consentAuditRepository
            .Setup(r => r.AddAsync(It.IsAny<ConsentAuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("AddAsync"))
            .Returns(Task.CompletedTask);

        UpdateConsentHandler sut = CreateSut();

        await sut.HandleAsync(ValidRequest());

        callOrder.Should().Equal("GetAsync", "UpdateAsync", "AddAsync");
    }
}
