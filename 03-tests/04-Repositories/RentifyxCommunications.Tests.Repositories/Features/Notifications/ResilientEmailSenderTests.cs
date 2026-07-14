using ErrorOr;
using FluentAssertions;
using Moq;
using Polly;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Email;
using RentifyxCommunications.Infrastructure.Options;
using RentifyxCommunications.Infrastructure.Resilience;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class ResilientEmailSenderTests
{
    private static readonly EmailAddress Recipient = EmailAddress.Create("user@example.com").Value;

    private static ResiliencePipeline<ErrorOr<Success>> BuildPipeline(ResilienceOptions options) =>
        ResiliencePipelineFactory.Create(options);

    [Fact]
    public async Task SendAsync_ShouldPassThroughUnchanged_WhenInnerSenderSucceeds()
    {
        Mock<IEmailSender> inner = new();
        inner.Setup(s => s.SendAsync(Recipient, "body", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        ResilientEmailSender sut = new(inner.Object, BuildPipeline(new ResilienceOptions()));

        ErrorOr<Success> result = await sut.SendAsync(Recipient, "body");

        result.IsError.Should().BeFalse();
        inner.Verify(s => s.SendAsync(Recipient, "body", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ShouldOpenCircuitAndStopCallingInner_AfterMinimumThroughputFailures()
    {
        Mock<IEmailSender> inner = new();
        inner.Setup(s => s.SendAsync(Recipient, "body", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Ses.SendFailed", "simulated failure"));

        ResilienceOptions options = new(
            TokenBucketPermitsPerSecond: 1000,
            TokenBucketQueueMaxWaitSeconds: 5,
            CircuitBreakerMinimumThroughput: 2,
            CircuitBreakerSamplingDurationSeconds: 60,
            CircuitBreakerBreakDurationSeconds: 60);
        ResilientEmailSender sut = new(inner.Object, BuildPipeline(options));

        await sut.SendAsync(Recipient, "body");
        await sut.SendAsync(Recipient, "body");
        ErrorOr<Success> thirdResult = await sut.SendAsync(Recipient, "body");

        thirdResult.IsError.Should().BeTrue();
        thirdResult.FirstError.Code.Should().Be(ResilienceErrorCodes.CircuitOpen);
        inner.Verify(s => s.SendAsync(Recipient, "body", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_ShouldMapRateLimitRejection_ToErrorOrFailure_NotUnhandledException()
    {
        Mock<IEmailSender> inner = new();
        inner.Setup(s => s.SendAsync(Recipient, "body", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        ResilienceOptions options = new(
            TokenBucketPermitsPerSecond: 1,
            TokenBucketQueueMaxWaitSeconds: 0,
            CircuitBreakerMinimumThroughput: 100,
            CircuitBreakerSamplingDurationSeconds: 60,
            CircuitBreakerBreakDurationSeconds: 60);
        ResilientEmailSender sut = new(inner.Object, BuildPipeline(options));

        await sut.SendAsync(Recipient, "body");
        ErrorOr<Success> secondResult = await sut.SendAsync(Recipient, "body");

        secondResult.IsError.Should().BeTrue();
        secondResult.FirstError.Code.Should().Be(ResilienceErrorCodes.RateLimitExceeded);
    }
}
