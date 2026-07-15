using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RentifyxCommunications.Infrastructure.Options;
using RentifyxCommunications.Infrastructure.Resilience;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class ResilienceStartupValidatorTests
{
    [Fact]
    public void Validate_ShouldNotThrow_WhenAllOptionsAreValid()
    {
        ResilienceStartupValidator sut = new(Options.Create(new ResilienceOptions()), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldNotThrow_WhenTokenBucketQueueMaxWaitSecondsIsZero()
    {
        ResilienceOptions options = new(TokenBucketQueueMaxWaitSeconds: 0);
        ResilienceStartupValidator sut = new(Options.Create(options), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ShouldThrow_WhenTokenBucketPermitsPerSecondIsZeroOrNegative(int value)
    {
        ResilienceOptions options = new(TokenBucketPermitsPerSecond: value);
        ResilienceStartupValidator sut = new(Options.Create(options), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ResilienceOptions.TokenBucketPermitsPerSecond)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ShouldThrow_WhenCircuitBreakerMinimumThroughputIsZeroOrNegative(int value)
    {
        ResilienceOptions options = new(CircuitBreakerMinimumThroughput: value);
        ResilienceStartupValidator sut = new(Options.Create(options), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ResilienceOptions.CircuitBreakerMinimumThroughput)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ShouldThrow_WhenCircuitBreakerSamplingDurationSecondsIsZeroOrNegative(int value)
    {
        ResilienceOptions options = new(CircuitBreakerSamplingDurationSeconds: value);
        ResilienceStartupValidator sut = new(Options.Create(options), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ResilienceOptions.CircuitBreakerSamplingDurationSeconds)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ShouldThrow_WhenCircuitBreakerBreakDurationSecondsIsZeroOrNegative(int value)
    {
        ResilienceOptions options = new(CircuitBreakerBreakDurationSeconds: value);
        ResilienceStartupValidator sut = new(Options.Create(options), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ResilienceOptions.CircuitBreakerBreakDurationSeconds)}*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenTokenBucketQueueMaxWaitSecondsIsNegative()
    {
        ResilienceOptions options = new(TokenBucketQueueMaxWaitSeconds: -1);
        ResilienceStartupValidator sut = new(Options.Create(options), Mock.Of<ILogger<ResilienceStartupValidator>>());

        Action act = sut.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ResilienceOptions.TokenBucketQueueMaxWaitSeconds)}*");
    }
}
