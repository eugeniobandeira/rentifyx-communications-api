namespace RentifyxCommunications.Infrastructure.Options;

public sealed record ReconciliationOptions(
    int PollIntervalSeconds = 60,
    int StalenessThresholdSeconds = 120);
