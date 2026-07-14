using ErrorOr;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Email;

public sealed class ResilientEmailSender(
    IEmailSender inner,
    ResiliencePipeline<ErrorOr<Success>> pipeline) : IEmailSender
{
    public async Task<ErrorOr<Success>> SendAsync(
        EmailAddress recipient,
        string renderedContent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pipeline.ExecuteAsync(
                async ct => await inner.SendAsync(recipient, renderedContent, ct),
                cancellationToken);
        }
        catch (RateLimiterRejectedException)
        {
            return Error.Failure(
                ResilienceErrorCodes.RateLimitExceeded,
                "Send rejected: SES rate limit exceeded and the queue wait timed out.");
        }
        catch (BrokenCircuitException)
        {
            return Error.Failure(
                ResilienceErrorCodes.CircuitOpen,
                "Send rejected: circuit breaker is open due to sustained SES failures.");
        }
    }
}
