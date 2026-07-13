using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RentifyxCommunications.Api.Consumers;

public sealed class NotificationRequestedConsumer : IHostedService, IDisposable
{
    internal const string Topic = "notification-requested";
    internal const int MaxStartupAttempts = 3;

    private readonly ILogger<NotificationRequestedConsumer> _logger;
    private readonly IKafkaConsumerFactory _consumerFactory;
    private readonly string _groupId;
    private readonly TimeSpan? _startupRetryDelayOverride;

    private IConsumer<Ignore, string>? _consumer;
    private CancellationTokenSource? _consumeLoopCts;
    private Task? _consumeLoopTask;

    public NotificationRequestedConsumer(
        ILogger<NotificationRequestedConsumer> logger,
        IKafkaConsumerFactory consumerFactory,
        IConfiguration configuration,
        TimeSpan? startupRetryDelayOverride = null)
    {
        _logger = logger;
        _consumerFactory = consumerFactory;
        _groupId = configuration["Kafka:ConsumerGroupId"] ?? "rentifyx-communications-api";
        _startupRetryDelayOverride = startupRetryDelayOverride;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxStartupAttempts; attempt++)
        {
            try
            {
                IConsumer<Ignore, string> consumer = _consumerFactory.Create();
                consumer.Subscribe(Topic);
                _consumer = consumer;

                _logger.LogInformation(
                    "NotificationRequestedConsumer subscribed to {Topic} (group: {GroupId})",
                    Topic,
                    _groupId);

                _consumeLoopCts = new CancellationTokenSource();
                _consumeLoopTask = Task.Run(() => ConsumeLoop(consumer, _consumeLoopCts.Token), CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to connect NotificationRequestedConsumer to Kafka (attempt {Attempt}/{MaxAttempts})",
                    attempt,
                    MaxStartupAttempts);

                if (attempt < MaxStartupAttempts)
                    await Task.Delay(_startupRetryDelayOverride ?? TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        _logger.LogError(
            "NotificationRequestedConsumer did not start after {MaxAttempts} attempts - it will not consume messages",
            MaxStartupAttempts);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_consumeLoopCts is not null)
            await _consumeLoopCts.CancelAsync();

        if (_consumeLoopTask is not null)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                await _consumeLoopTask.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                // Best-effort drain within the timeout; close the consumer regardless.
            }
        }

        _consumeLoopCts?.Dispose();
        _consumer?.Close();
        _logger.LogInformation("NotificationRequestedConsumer stopped");
    }

    private static void ConsumeLoop(IConsumer<Ignore, string> consumer, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ConsumeResult<Ignore, string>? result = consumer.Consume(TimeSpan.FromSeconds(1));

            if (result is null || result.IsPartitionEOF)
                continue;

            // No processing logic yet - E-03 injects the dispatch pipeline here.
            consumer.Commit(result);
        }
    }

    public void Dispose()
    {
        _consumeLoopCts?.Dispose();
        _consumer?.Dispose();
    }
}
