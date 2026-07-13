using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Events;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.Entities;

public sealed class NotificationTerminalTransitionTests
{
    private static Notification CreatePending() =>
        Notification.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("user@example.com").Value,
            Channel.Email,
            TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;

    private static Notification CreateDispatching()
    {
        Notification notification = CreatePending();
        notification.Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: true);
        return notification;
    }

    [Fact]
    public void MarkSent_FromDispatching_ShouldSucceed()
    {
        Notification notification = CreateDispatching();

        ErrorOr<Success> result = notification.MarkSent();

        result.IsError.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.DomainEvents.Should().ContainSingle(e => e is NotificationDelivered);
    }

    [Fact]
    public void MarkFailed_FromDispatching_ShouldSucceedAndStoreReason()
    {
        Notification notification = CreateDispatching();

        ErrorOr<Success> result = notification.MarkFailed("SES throttled");

        result.IsError.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.FailureReason.Should().Be("SES throttled");
        notification.DomainEvents.OfType<NotificationFailed>().Should().ContainSingle(f => f.Reason == "SES throttled");
    }

    [Fact]
    public void MarkSent_FromPending_ShouldReturnInvalidTransitionError()
    {
        Notification notification = CreatePending();

        ErrorOr<Success> result = notification.MarkSent();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidTransition);
    }

    [Fact]
    public void MarkFailed_OnAlreadySent_ShouldReturnAlreadyTerminalError()
    {
        Notification notification = CreateDispatching();
        notification.MarkSent();

        ErrorOr<Success> result = notification.MarkFailed("late failure");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.AlreadyTerminal);
    }

    [Fact]
    public void MarkSent_OnAlreadyFailed_ShouldReturnAlreadyTerminalError()
    {
        Notification notification = CreateDispatching();
        notification.MarkFailed("initial failure");

        ErrorOr<Success> result = notification.MarkSent();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.AlreadyTerminal);
    }

    [Fact]
    public void MarkSent_OnSuppressed_ShouldReturnAlreadyTerminalError()
    {
        Notification notification = CreatePending();
        ConsentPreference optedOut = ConsentPreference.Create(Guid.NewGuid(), Channel.Email, optedIn: false, DateTime.UtcNow).Value;
        notification.Dispatch(ConsentDecision.FromPreference(optedOut), isPayloadValid: true);

        ErrorOr<Success> result = notification.MarkSent();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.AlreadyTerminal);
    }
}
