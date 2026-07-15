namespace RentifyxCommunications.Api.Extensions.Options;

public sealed record RateLimitOptions(
    int PermitLimit = 100,
    int WindowSeconds = 60,
    int QueueLimit = 0);
