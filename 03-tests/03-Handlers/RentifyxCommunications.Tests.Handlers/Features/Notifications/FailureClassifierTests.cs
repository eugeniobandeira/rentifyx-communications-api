using System.Text.Json;
using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Features.Notifications;

public sealed class FailureClassifierTests
{
    [Theory]
    [InlineData("Dispatch.CorrelationIdRequired")]
    [InlineData("Notification.InvalidTransition")]
    [InlineData("Notification.InvalidEmailAddress")]
    [InlineData("Notification.InvalidTemplateId")]
    [InlineData("Notification.InvalidRecipientId")]
    public void Classify_Errors_ShouldReturnPoisonPill_ForMalformedMessageCodes(string code)
    {
        FailureClassification result = FailureClassifier.Classify([Error.Validation(code, "message")]);

        result.Should().Be(FailureClassification.PoisonPill);
    }

    [Theory]
    [InlineData("Template.NotFound")]
    [InlineData("Template.MissingField")]
    [InlineData("Template.ParseError")]
    public void Classify_Errors_ShouldReturnPoisonPill_ForTemplateErrorCodes(string code)
    {
        FailureClassification result = FailureClassifier.Classify([Error.Failure(code, "message")]);

        result.Should().Be(FailureClassification.PoisonPill);
    }

    [Fact]
    public void Classify_Errors_ShouldReturnTransient_ForSesSendFailed()
    {
        FailureClassification result = FailureClassifier.Classify([Error.Failure(SesErrorCodes.SendFailed, "message")]);

        result.Should().Be(FailureClassification.Transient);
    }

    [Theory]
    [InlineData("Resilience.RateLimitExceeded")]
    [InlineData("Resilience.CircuitOpen")]
    public void Classify_Errors_ShouldReturnTransient_ForResilienceErrorCodes(string code)
    {
        FailureClassification result = FailureClassifier.Classify([Error.Failure(code, "message")]);

        result.Should().Be(FailureClassification.Transient);
    }

    [Fact]
    public void Classify_Errors_ShouldReturnPoisonPill_ForAnUnmatchedCode()
    {
        FailureClassification result = FailureClassifier.Classify([Error.Failure("Some.UnknownCode", "message")]);

        result.Should().Be(FailureClassification.PoisonPill);
    }

    [Fact]
    public void Classify_Errors_ShouldReturnPoisonPill_ForAnEmptyErrorList()
    {
        FailureClassification result = FailureClassifier.Classify([]);

        result.Should().Be(FailureClassification.PoisonPill);
    }

    [Fact]
    public void Classify_Exception_ShouldReturnPoisonPill_ForJsonException()
    {
        FailureClassification result = FailureClassifier.Classify(new JsonException("malformed"));

        result.Should().Be(FailureClassification.PoisonPill);
    }

    [Fact]
    public void Classify_Exception_ShouldReturnTransient_ForAnyOtherException()
    {
        FailureClassification result = FailureClassifier.Classify(new InvalidOperationException("db unreachable"));

        result.Should().Be(FailureClassification.Transient);
    }
}
