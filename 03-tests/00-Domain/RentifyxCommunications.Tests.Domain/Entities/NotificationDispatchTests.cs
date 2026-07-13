using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Events;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.Entities;

public sealed class NotificationDispatchTests
{
    private static Notification CreatePending(Channel channel = Channel.Email) =>
        Notification.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("user@example.com").Value,
            channel,
            TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;

    [Fact]
    public void Dispatch_WithNoConsentRecordAndValidPayload_ShouldMoveToDispatching()
    {
        Notification notification = CreatePending();

        ErrorOr<Success> result = notification.Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: true);

        result.IsError.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatus.Dispatching);
        notification.DomainEvents.Should().ContainSingle(e => e is NotificationDispatched);
    }

    [Fact]
    public void Dispatch_WhenSuppressed_ShouldMoveToSuppressedAndNotError()
    {
        Notification notification = CreatePending();
        ConsentPreference optedOut = ConsentPreference.Create(Guid.NewGuid(), Channel.Email, optedIn: false, DateTime.UtcNow).Value;

        ErrorOr<Success> result = notification.Dispatch(ConsentDecision.FromPreference(optedOut), isPayloadValid: true);

        result.IsError.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatus.Suppressed);
        notification.DomainEvents.Should().ContainSingle(e => e is NotificationSuppressed);
    }

    [Fact]
    public void Dispatch_WithInvalidPayload_ShouldReturnErrorAndStayPending()
    {
        Notification notification = CreatePending();

        ErrorOr<Success> result = notification.Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: false);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidPayload);
        notification.Status.Should().Be(NotificationStatus.Pending);
    }

    [Theory]
    [InlineData(Channel.Sms)]
    [InlineData(Channel.Push)]
    public void Dispatch_WithUnimplementedChannel_ShouldReturnErrorAndStayPending(Channel channel)
    {
        Notification notification = CreatePending(channel);

        ErrorOr<Success> result = notification.Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: true);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.ChannelNotImplemented);
        notification.Status.Should().Be(NotificationStatus.Pending);
    }

    [Fact]
    public void Dispatch_CalledTwice_ShouldReturnErrorOnSecondCall()
    {
        Notification notification = CreatePending();
        notification.Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: true);

        ErrorOr<Success> secondCall = notification.Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: true);

        secondCall.IsError.Should().BeTrue();
        secondCall.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidTransition);
        notification.DomainEvents.Should().ContainSingle();
    }
}
