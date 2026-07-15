namespace RentifyxCommunications.Domain.ValueObjects;

public sealed record RetryContext(
    string OriginalTopic,
    int RetryCount = 0,
    DateTimeOffset? FirstFailureTimestamp = null);
