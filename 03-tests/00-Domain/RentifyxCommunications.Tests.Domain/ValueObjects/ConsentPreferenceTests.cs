using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.ValueObjects;

public sealed class ConsentPreferenceTests
{
    [Fact]
    public void Create_WithValidRecipientId_ShouldSucceed()
    {
        Guid recipientId = Guid.NewGuid();
        DateTime updatedAt = DateTime.UtcNow;

        ErrorOr<ConsentPreference> result = ConsentPreference.Create(recipientId, Channel.Email, optedIn: true, updatedAt);

        result.IsError.Should().BeFalse();
        result.Value.RecipientId.Should().Be(recipientId);
        result.Value.Channel.Should().Be(Channel.Email);
        result.Value.OptedIn.Should().BeTrue();
        result.Value.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Create_WithEmptyRecipientId_ShouldReturnValidationError()
    {
        ErrorOr<ConsentPreference> result = ConsentPreference.Create(Guid.Empty, Channel.Email, optedIn: false, DateTime.UtcNow);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidRecipientId);
    }
}
