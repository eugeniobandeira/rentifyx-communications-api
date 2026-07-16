namespace RentifyxCommunications.Api.Extensions.Options;

public sealed record RateLimitOptions(
    int PermitLimit = 100,
    int WindowSeconds = 60,
    int QueueLimit = 0,
    ConsentRateLimitOptions? Consent = null)
{
    public ConsentRateLimitOptions Consent { get; init; } = Consent ?? new ConsentRateLimitOptions();
}

public sealed record ConsentRateLimitOptions(
    int PermitLimit = 10,
    int WindowSeconds = 60,
    int QueueLimit = 0);
