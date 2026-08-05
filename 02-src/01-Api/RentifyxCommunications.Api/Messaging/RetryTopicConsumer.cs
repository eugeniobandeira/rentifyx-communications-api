using System.Diagnostics;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Common;
using RentifyxCommunications.Domain.ValueObjects;
using Serilog.Context;

namespace RentifyxCommunications.Api.Messaging;

public sealed class RetryTopicConsumer(
    string topic,
    ILogger<RetryTopicConsumer> logger,
    IKafkaConsumerFactory consumerFactory,
    IServiceScopeFactory scopeFactory) : IHostedService, IDisposable
{
    private const string OriginalTopicHeader = "x-original-topic";
    private const string RetryCountHeader = "x-retry-count";
    private const string FirstFailureTimestampHeader = "x-first-failure-timestamp";
    private const string NextRetryAtHeader = "x-next-retry-at";

    private IConsumer<Ignore, string>? _consumer;
    private CancellationTokenSource? _consumeLoopCts;
    private Task? _consumeLoopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IConsumer<Ignore, string> consumer = consumerFactory.Create(topic);
        consumer.Subscribe(topic);
        _consumer = consumer;

        logger.LogInformation("RetryTopicConsumer subscribed to {Topic}", topic);

        _consumeLoopCts = new CancellationTokenSource();
        _consumeLoopTask = Task.Run(() => ConsumeLoopAsync(consumer, _consumeLoopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
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
        logger.LogInformation("RetryTopicConsumer for {Topic} stopped", topic);
    }

    private async Task ConsumeLoopAsync(IConsumer<Ignore, string> consumer, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ConsumeResult<Ignore, string>? result = consumer.Consume(KafkaConsumerHostedServiceDefaults.ConsumePollTimeout);

                if (result is null || result.IsPartitionEOF)
                    continue;

                await ProcessMessageAsync(result, token);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Expected on shutdown.
            }
            catch (Exception ex)
            {
                // consumer.Consume() itself can throw (e.g. a deserialization or
                // broker-protocol error) - uncaught, this silently kills the whole
                // loop task forever (librdkafka's background heartbeat thread keeps
                // the group membership alive regardless, masking the failure as a
                // healthy-but-stuck consumer with no error logged anywhere).
                logger.LogError(ex, "Consume loop tick failed on {Topic}, will retry next tick.", topic);
            }
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<Ignore, string> result, CancellationToken token)
    {
        try
        {
            DateTimeOffset? nextRetryAt = ReadTimestampHeader(result.Message.Headers, NextRetryAtHeader);
            if (nextRetryAt is not null)
            {
                TimeSpan remaining = nextRetryAt.Value - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, token);
            }

            ActivityContext parentContext = TraceContextHeaders.Extract(result.Message.Headers, logger);

            using Activity? activity = MessagingActivitySource.Instance.StartActivity(
                "retry-topic consume",
                ActivityKind.Consumer,
                parentContext);

            using IDisposable traceIdScope = LogContext.PushProperty("TraceId", activity?.TraceId.ToString());
            using IDisposable spanIdScope = LogContext.PushProperty("SpanId", activity?.SpanId.ToString());

            RetryContext context = BuildRetryContext(result.Message.Headers) with
            {
                TraceParent = activity?.Id,
                TraceState = activity?.TraceStateString
            };

            using IServiceScope scope = scopeFactory.CreateScope();
            NotificationDispatchProcessor processor = scope.ServiceProvider.GetRequiredService<NotificationDispatchProcessor>();

            await processor.ProcessAsync(result.Message.Value, context, token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error routing a retry-stage message on {Topic}. Partition={Partition} Offset={Offset}",
                topic,
                result.Partition.Value,
                result.Offset.Value);
        }
    }

    private RetryContext BuildRetryContext(Headers headers)
    {
        string originalTopic = ReadStringHeader(headers, OriginalTopicHeader) ?? topic;
        int retryCount = int.TryParse(ReadStringHeader(headers, RetryCountHeader), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
        DateTimeOffset? firstFailureTimestamp = ReadTimestampHeader(headers, FirstFailureTimestampHeader);

        return new RetryContext(originalTopic, retryCount, firstFailureTimestamp);
    }

    private static string? ReadStringHeader(Headers headers, string key) =>
        headers.TryGetLastBytes(key, out byte[] bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private static DateTimeOffset? ReadTimestampHeader(Headers headers, string key)
    {
        string? value = ReadStringHeader(headers, key);
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    public void Dispose()
    {
        _consumeLoopCts?.Dispose();
        _consumer?.Dispose();
    }
}
