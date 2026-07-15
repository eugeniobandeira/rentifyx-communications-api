namespace RentifyxCommunications.Domain.Constants;

public static class RetryTopicChain
{
    public const string OriginalTopic = "notification-requested";
    public const string Retry5sTopic = "notification-requested-retry-5s";
    public const string Retry1mTopic = "notification-requested-retry-1m";
    public const string Retry10mTopic = "notification-requested-retry-10m";
    public const string DlqTopic = "notification-requested-dlq";

    public static string NextStage(int currentRetryCount) => currentRetryCount switch
    {
        0 => Retry5sTopic,
        1 => Retry1mTopic,
        2 => Retry10mTopic,
        _ => DlqTopic
    };

    public static TimeSpan DelayFor(string topic) => topic switch
    {
        Retry5sTopic => TimeSpan.FromSeconds(5),
        Retry1mTopic => TimeSpan.FromMinutes(1),
        Retry10mTopic => TimeSpan.FromMinutes(10),
        _ => throw new ArgumentException($"'{topic}' has no configured retry delay.", nameof(topic))
    };
}
