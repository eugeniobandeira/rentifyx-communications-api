namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;

public sealed record RetryContext(
    string OriginalTopic,
    int RetryCount = 0,
    DateTimeOffset? FirstFailureTimestamp = null);
