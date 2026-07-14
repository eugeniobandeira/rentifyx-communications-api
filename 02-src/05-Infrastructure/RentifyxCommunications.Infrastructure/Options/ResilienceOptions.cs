namespace RentifyxCommunications.Infrastructure.Options;

public sealed record ResilienceOptions(
    int TokenBucketPermitsPerSecond = 14,
    int TokenBucketQueueMaxWaitSeconds = 5,
    int CircuitBreakerMinimumThroughput = 5,
    int CircuitBreakerSamplingDurationSeconds = 30,
    int CircuitBreakerBreakDurationSeconds = 30);
