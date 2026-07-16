using ErrorOr;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Response;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Validator;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Consent;

public sealed class GetConsentHandlerTests
{
    private readonly Mock<IConsentRepository> _consentRepository = new();

    private static GetConsentRequest ValidRequest(Guid? recipientId = null)
    {
        return new GetConsentRequest(
            (recipientId ?? Guid.NewGuid()).ToString(),
            "Email");
    }

    private GetConsentHandler CreateSut()
    {
        return new GetConsentHandler(
            new GetConsentValidator(),
            _consentRepository.Object);
    }

    [Fact]
    public async Task Handle_WhenConsentRecordExistsAndOptedOut_ShouldReturnResponseReflectingStoredValue()
    {
        Guid recipientId = Guid.NewGuid();
        ConsentPreference optedOut = ConsentPreference.Create(recipientId, Channel.Email, optedIn: false, DateTime.UtcNow).Value;
        _consentRepository
            .Setup(r => r.GetAsync(recipientId, Channel.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(optedOut);

        GetConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(ValidRequest(recipientId));

        result.IsError.Should().BeFalse();
        result.Value.OptedIn.Should().BeFalse();
        result.Value.UpdatedAt.Should().Be(optedOut.UpdatedAt);
    }

    [Fact]
    public async Task Handle_WhenNoConsentRecordExists_ShouldReturnOptedInTrueByDefault()
    {
        _consentRepository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentPreference?)null);

        GetConsentHandler sut = CreateSut();

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(ValidRequest());

        result.IsError.Should().BeFalse();
        result.Value.OptedIn.Should().BeTrue();
        result.Value.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithMalformedRecipientId_ShouldReturnValidationErrorWithoutTouchingRepository()
    {
        GetConsentHandler sut = CreateSut();
        GetConsentRequest request = ValidRequest() with { RecipientId = "not-a-guid" };

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeTrue();
        _consentRepository.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidChannel_ShouldReturnValidationErrorWithoutTouchingRepository()
    {
        GetConsentHandler sut = CreateSut();
        GetConsentRequest request = ValidRequest() with { Channel = "not-a-channel" };

        ErrorOr<ConsentResponse> result = await sut.HandleAsync(request);

        result.IsError.Should().BeTrue();
        _consentRepository.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
