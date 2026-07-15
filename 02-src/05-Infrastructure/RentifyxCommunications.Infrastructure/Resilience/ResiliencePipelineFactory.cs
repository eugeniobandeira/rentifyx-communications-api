using System.Threading.RateLimiting;
using ErrorOr;
using Polly;
using Polly.CircuitBreaker;
using RentifyxCommunications.Infrastructure.Options;

namespace RentifyxCommunications.Infrastructure.Resilience;

public static class ResiliencePipelineFactory
{
    public static ResiliencePipeline<ErrorOr<Success>> Create(ResilienceOptions options)
    {
        PredicateBuilder<ErrorOr<Success>> shouldHandle = new PredicateBuilder<ErrorOr<Success>>()
            .HandleResult(result => result.IsError);

#pragma warning disable CA2000 // The rate limiter's lifetime is the returned pipeline's lifetime, which is
        // registered as a process-wide Singleton (T06) — there is no earlier disposal
        // boundary to hook into without switching to Polly's registry-based DI
        // integration (OnPipelineDisposed), which the design deliberately avoided to
        // keep pipeline construction a plain, directly-testable static method.
        TokenBucketRateLimiter rateLimiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.TokenBucketPermitsPerSecond,
            TokensPerPeriod = options.TokenBucketPermitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = options.TokenBucketPermitsPerSecond * options.TokenBucketQueueMaxWaitSeconds,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
#pragma warning restore CA2000

        return new ResiliencePipelineBuilder<ErrorOr<Success>>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<ErrorOr<Success>>
            {
                ShouldHandle = shouldHandle,
                FailureRatio = 1.0,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds)
            })
            .AddRateLimiter(rateLimiter)
            .Build();
    }
}
