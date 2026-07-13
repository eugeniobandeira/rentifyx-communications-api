using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.ValueObjects;

public sealed class EmailAddressTests
{
    [Fact]
    public void Create_WithValidAddress_ShouldSucceed()
    {
        ErrorOr<EmailAddress> result = EmailAddress.Create("user@example.com");

        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("user@example.com");
    }

    [Fact]
    public void Create_WithInvalidFormat_ShouldReturnValidationError()
    {
        ErrorOr<EmailAddress> result = EmailAddress.Create("not-an-email");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidEmailAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrWhitespace_ShouldReturnValidationError(string? value)
    {
        ErrorOr<EmailAddress> result = EmailAddress.Create(value!);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidEmailAddress);
    }
}
