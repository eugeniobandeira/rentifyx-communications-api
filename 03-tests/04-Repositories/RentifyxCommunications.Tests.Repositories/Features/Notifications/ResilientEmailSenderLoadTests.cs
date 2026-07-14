using System.Diagnostics;
using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Email;
using RentifyxCommunications.Infrastructure.Options;
using RentifyxCommunications.Infrastructure.Resilience;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

/// <summary>
/// On-demand evidence for spec THR-08 — not part of the default CI gate (excluded from
/// "Category!=Integration&amp;Category!=LoadTest", the CI filter). Run manually with:
/// dotnet test --filter "Category=LoadTest"
/// </summary>
[Trait("Category", "LoadTest")]
public sealed class ResilientEmailSenderLoadTests
{
    private const int BurstSize = 1000;
    private const int TokenBucketPermitsPerSecond = 200;

    [Fact]
    public async Task SendAsync_ShouldThrottleToConfiguredRate_WhenFiring1000NotificationsInABurst()
    {
        EmailAddress recipient = EmailAddress.Create("user@example.com").Value;
        CountingEmailSender inner = new();

        ResilienceOptions options = new(
            TokenBucketPermitsPerSecond: TokenBucketPermitsPerSecond,
            TokenBucketQueueMaxWaitSeconds: 10,
            CircuitBreakerMinimumThroughput: BurstSize + 1,
            CircuitBreakerSamplingDurationSeconds: 60,
            CircuitBreakerBreakDurationSeconds: 60);
        ResilientEmailSender sut = new(inner, ResiliencePipelineFactory.Create(options));

        Stopwatch stopwatch = Stopwatch.StartNew();

        IEnumerable<Task<ErrorOr<Success>>> sends = Enumerable.Range(0, BurstSize)
            .Select(_ => sut.SendAsync(recipient, "body"));
        ErrorOr<Success>[] results = await Task.WhenAll(sends);

        stopwatch.Stop();

        results.Should().OnlyContain(r => !r.IsError, "the queue was sized to absorb the full burst without rejections");
        inner.CallCount.Should().Be(BurstSize);

        // Only the sends beyond the initially-full bucket's capacity must wait for refills.
        double theoreticalMinimumSeconds = (double)(BurstSize - TokenBucketPermitsPerSecond) / TokenBucketPermitsPerSecond;
        stopwatch.Elapsed.TotalSeconds.Should().BeGreaterThanOrEqualTo(
            theoreticalMinimumSeconds * 0.8,
            "the token bucket must actually throttle the burst to roughly {0} calls/second, not let it through instantly",
            TokenBucketPermitsPerSecond);
    }

    private sealed class CountingEmailSender : IEmailSender
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ErrorOr<Success>> SendAsync(
            EmailAddress recipient,
            string renderedContent,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }
}
