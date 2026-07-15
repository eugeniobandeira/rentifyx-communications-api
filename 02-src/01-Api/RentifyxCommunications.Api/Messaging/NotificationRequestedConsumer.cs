using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Api.Messaging;

public sealed class NotificationRequestedConsumer(
    ILogger<NotificationRequestedConsumer> logger,
    IKafkaConsumerFactory consumerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    NotificationMetrics? metrics = null,
    TimeSpan? startupRetryDelayOverride = null) : IHostedService, IDisposable
{
    internal const string Topic = RetryTopicChain.OriginalTopic;
    internal const int MaxStartupAttempts = 3;

    private readonly ILogger<NotificationRequestedConsumer> _logger = logger;
    private readonly IKafkaConsumerFactory _consumerFactory = consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly string _groupId = kafkaOptions.Value.ConsumerGroupId;
    private readonly Action<long>? _setConsumerLag = metrics is null ? null : metrics.SetConsumerLag;
    private readonly TimeSpan? _startupRetryDelayOverride = startupRetryDelayOverride;

    private IConsumer<Ignore, string>? _consumer;
    private CancellationTokenSource? _consumeLoopCts;
    private Task? _consumeLoopTask;

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
                _consumeLoopTask = Task.Run(() => ConsumeLoopAsync(consumer, _consumeLoopCts.Token), CancellationToken.None);
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
            using CancellationTokenSource timeout = new(KafkaConsumerHostedServiceDefaults.ShutdownDrainTimeout);
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

    private async Task ConsumeLoopAsync(IConsumer<Ignore, string> consumer, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ConsumeResult<Ignore, string>? result = consumer.Consume(KafkaConsumerHostedServiceDefaults.ConsumePollTimeout);

            if (result is null || result.IsPartitionEOF)
                continue;

            await ProcessMessageAsync(result, token);
            consumer.Commit(result);
            UpdateConsumerLag(consumer, result.TopicPartition, result.Offset.Value);
        }
    }

    private void UpdateConsumerLag(IConsumer<Ignore, string> consumer, TopicPartition partition, long consumedOffset)
    {
        if (_setConsumerLag is null)
            return;

        try
        {
            WatermarkOffsets watermarks = consumer.GetWatermarkOffsets(partition);
            long lag = Math.Max(0, watermarks.High.Value - consumedOffset - 1);
            _setConsumerLag(lag);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute consumer lag for {Partition}", partition);
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<Ignore, string> result, CancellationToken token)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            NotificationDispatchProcessor processor = scope.ServiceProvider.GetRequiredService<NotificationDispatchProcessor>();

            RetryContext context = new(Topic);
            await processor.ProcessAsync(result.Message.Value, context, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error routing a NotificationRequested message. Partition={Partition} Offset={Offset}",
                result.Partition.Value,
                result.Offset.Value);
        }
    }

    public void Dispose()
    {
        _consumeLoopCts?.Dispose();
        _consumer?.Dispose();
    }
}
