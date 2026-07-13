using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.Entities;

public sealed class NotificationCreateTests
{
    private static EmailAddress ValidEmail() => EmailAddress.Create("user@example.com").Value;
    private static TemplateId ValidTemplateId() => TemplateId.Create("welcome-email").Value;
    private static Dictionary<string, string> ValidPayload() => new() { ["name"] = "Alice" };

    [Fact]
    public void Create_WithValidInputs_ShouldStartAsPending()
    {
        ErrorOr<Notification> result = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValidEmail(), Channel.Email, ValidTemplateId(), ValidPayload());

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(NotificationStatus.Pending);
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public void Create_WithEmptyPayload_ShouldReturnValidationError()
    {
        ErrorOr<Notification> result = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValidEmail(), Channel.Email, ValidTemplateId(), new Dictionary<string, string>());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidPayload);
    }

    [Fact]
    public void Create_ShouldNotRaiseAnyDomainEvents()
    {
        ErrorOr<Notification> result = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValidEmail(), Channel.Email, ValidTemplateId(), ValidPayload());

        result.Value.DomainEvents.Should().BeEmpty();
    }
}
