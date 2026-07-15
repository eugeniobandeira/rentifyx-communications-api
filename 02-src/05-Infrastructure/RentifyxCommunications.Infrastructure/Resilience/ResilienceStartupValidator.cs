using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Infrastructure.Options;

namespace RentifyxCommunications.Infrastructure.Resilience;

public sealed class ResilienceStartupValidator(
    IOptions<ResilienceOptions> resilienceOptions,
    ILogger<ResilienceStartupValidator> logger)
{
    public void Validate()
    {
        ResilienceOptions options = resilienceOptions.Value;
        // TokenBucketQueueMaxWaitSeconds is intentionally excluded: 0 is a valid, meaningful setting
        // (reject immediately instead of queueing when the bucket is empty), not a misconfiguration.
        (string Name, int Value)[] requiredPositiveValues =
        [
            (nameof(ResilienceOptions.TokenBucketPermitsPerSecond), options.TokenBucketPermitsPerSecond),
            (nameof(ResilienceOptions.CircuitBreakerMinimumThroughput), options.CircuitBreakerMinimumThroughput),
            (nameof(ResilienceOptions.CircuitBreakerSamplingDurationSeconds), options.CircuitBreakerSamplingDurationSeconds),
            (nameof(ResilienceOptions.CircuitBreakerBreakDurationSeconds), options.CircuitBreakerBreakDurationSeconds),
        ];

        foreach ((string name, int value) in requiredPositiveValues)
        {
            if (value > 0)
                continue;

            logger.LogCritical(
                "ResilienceOptions.{SettingName} must be greater than zero, but was {Value}. Startup aborted.",
                name,
                value);
            throw new InvalidOperationException(
                $"ResilienceOptions.{name} must be greater than zero, but was {value}. Startup aborted.");
        }

        if (options.TokenBucketQueueMaxWaitSeconds < 0)
        {
            logger.LogCritical(
                "ResilienceOptions.{SettingName} must be zero or greater, but was {Value}. Startup aborted.",
                nameof(ResilienceOptions.TokenBucketQueueMaxWaitSeconds),
                options.TokenBucketQueueMaxWaitSeconds);
            throw new InvalidOperationException(
                $"ResilienceOptions.{nameof(ResilienceOptions.TokenBucketQueueMaxWaitSeconds)} must be zero or greater, " +
                $"but was {options.TokenBucketQueueMaxWaitSeconds}. Startup aborted.");
        }
    }
}
