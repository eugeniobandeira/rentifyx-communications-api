using ErrorOr;
using FluentAssertions;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using RentifyxCommunications.Infrastructure.Options;
using RentifyxCommunications.Infrastructure.Resilience;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class ResiliencePipelineFactoryTests
{
    [Fact]
    public async Task Create_ShouldRejectSend_WhenTokenBucketIsExhaustedAndQueueIsFull()
    {
        ResilienceOptions options = new(
            TokenBucketPermitsPerSecond: 1,
            TokenBucketQueueMaxWaitSeconds: 0,
            CircuitBreakerMinimumThroughput: 100,
            CircuitBreakerSamplingDurationSeconds: 60,
            CircuitBreakerBreakDurationSeconds: 60);

        ResiliencePipeline<ErrorOr<Success>> pipeline = ResiliencePipelineFactory.Create(options);

        ErrorOr<Success> first = await pipeline.ExecuteAsync(static _ => new ValueTask<ErrorOr<Success>>(Result.Success));
        first.IsError.Should().BeFalse();

        Func<Task> secondCall = async () =>
            await pipeline.ExecuteAsync(static _ => new ValueTask<ErrorOr<Success>>(Result.Success)).AsTask();

        await secondCall.Should().ThrowAsync<RateLimiterRejectedException>();
    }

    [Fact]
    public async Task Create_ShouldOpenCircuit_AfterMinimumThroughputFailuresWithinSamplingDuration()
    {
        ResilienceOptions options = new(
            TokenBucketPermitsPerSecond: 1000,
            TokenBucketQueueMaxWaitSeconds: 5,
            CircuitBreakerMinimumThroughput: 2,
            CircuitBreakerSamplingDurationSeconds: 60,
            CircuitBreakerBreakDurationSeconds: 60);

        ResiliencePipeline<ErrorOr<Success>> pipeline = ResiliencePipelineFactory.Create(options);
        int callCount = 0;

        for (int i = 0; i < 2; i++)
        {
            ErrorOr<Success> result = await pipeline.ExecuteAsync(_ =>
            {
                callCount++;
                return new ValueTask<ErrorOr<Success>>(Error.Failure("Test.Failure", "simulated failure"));
            });

            result.IsError.Should().BeTrue();
        }

        callCount.Should().Be(2);

        Func<Task> thirdCall = async () =>
            await pipeline.ExecuteAsync(_ =>
            {
                callCount++;
                return new ValueTask<ErrorOr<Success>>(Result.Success);
            }).AsTask();

        await thirdCall.Should().ThrowAsync<BrokenCircuitException>();
        callCount.Should().Be(2, "the circuit-broken call must never reach the wrapped delegate");
    }
}
