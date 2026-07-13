using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.ValueObjects;

public sealed class TemplateIdTests
{
    [Fact]
    public void Create_WithValidValue_ShouldSucceed()
    {
        ErrorOr<TemplateId> result = TemplateId.Create("welcome-email");

        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("welcome-email");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrWhitespace_ShouldReturnValidationError(string? value)
    {
        ErrorOr<TemplateId> result = TemplateId.Create(value!);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(NotificationErrorCodes.InvalidTemplateId);
    }
}
