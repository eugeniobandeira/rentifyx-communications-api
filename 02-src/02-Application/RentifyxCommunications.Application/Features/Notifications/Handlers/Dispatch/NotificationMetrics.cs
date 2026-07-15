using System.Diagnostics.Metrics;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;

/// <summary>
/// OTEL instrumentation for the notification dispatch pipeline. Registered as a Singleton
/// (one Meter per process) and injected wherever a metric needs to be recorded.
/// </summary>
public sealed class NotificationMetrics : IDisposable
{
    public const string MeterName = "RentifyxCommunications.Notifications";

    private readonly Meter _meter;
    private readonly Histogram<double> _dispatchDuration;
    private long _consumerLag;

    public NotificationMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _dispatchDuration = _meter.CreateHistogram<double>(
            "notification_dispatch_duration_seconds",
            unit: "s",
            description: "Elapsed time from message receipt to dispatch outcome.");
        _meter.CreateObservableGauge(
            "kafka_consumer_lag_notification_requested",
            () => Interlocked.Read(ref _consumerLag),
            description: "Lag (high watermark minus current position) on the notification-requested consumer group.");
    }

    public void RecordDispatchDuration(double seconds) => _dispatchDuration.Record(seconds);

    public void SetConsumerLag(long lag) => Interlocked.Exchange(ref _consumerLag, lag);

    public void Dispose() => _meter.Dispose();
}
