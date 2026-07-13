using FluentAssertions;
using FluentValidation.Results;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Validator;
using RentifyxCommunications.Domain.Constants;
using Xunit;

namespace RentifyxCommunications.Tests.Validators.Features.Notifications;

public sealed class DispatchNotificationValidatorTests
{
    private readonly DispatchNotificationValidator _validator = new();

    private static DispatchNotificationRequest ValidRequest() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "user@example.com",
        "Email",
        "welcome-email",
        new Dictionary<string, string> { ["name"] = "Alice" });

    [Fact]
    public void Validate_WithValidRequest_ShouldPass()
    {
        ValidationResult result = _validator.Validate(ValidRequest());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyCorrelationId_ShouldFail()
    {
        ValidationResult result = _validator.Validate(ValidRequest() with { CorrelationId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DispatchValidationErrorCodes.CorrelationIdRequired);
    }

    [Fact]
    public void Validate_WithEmptyRecipientId_ShouldFail()
    {
        ValidationResult result = _validator.Validate(ValidRequest() with { RecipientId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DispatchValidationErrorCodes.RecipientIdRequired);
    }

    [Fact]
    public void Validate_WithEmptyRecipientEmail_ShouldFail()
    {
        ValidationResult result = _validator.Validate(ValidRequest() with { RecipientEmail = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DispatchValidationErrorCodes.RecipientEmailRequired);
    }

    [Fact]
    public void Validate_WithEmptyTemplateId_ShouldFail()
    {
        ValidationResult result = _validator.Validate(ValidRequest() with { TemplateId = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DispatchValidationErrorCodes.TemplateIdRequired);
    }

    [Fact]
    public void Validate_WithUnrecognizedChannel_ShouldFail()
    {
        ValidationResult result = _validator.Validate(ValidRequest() with { Channel = "Carrier-Pigeon" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DispatchValidationErrorCodes.InvalidChannel);
    }

    [Fact]
    public void Validate_WithEmptyPayload_ShouldFail()
    {
        ValidationResult result = _validator.Validate(ValidRequest() with { Payload = new Dictionary<string, string>() });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DispatchValidationErrorCodes.PayloadRequired);
    }
}
