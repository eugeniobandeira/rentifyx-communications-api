using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;

namespace RentifyxCommunications.Api.Messaging;

public sealed class DlqObserverHostedService(
    ILogger<DlqObserverHostedService> logger,
    IKafkaConsumerFactory consumerFactory,
    IServiceScopeFactory scopeFactory) : IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IConsumer<Ignore, string>? _consumer;
    private CancellationTokenSource? _consumeLoopCts;
    private Task? _consumeLoopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IConsumer<Ignore, string> consumer = consumerFactory.Create();
        consumer.Subscribe(RetryTopicChain.DlqTopic);
        _consumer = consumer;

        logger.LogInformation("DlqObserverHostedService subscribed to {Topic}", RetryTopicChain.DlqTopic);

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
        logger.LogInformation("DlqObserverHostedService stopped");
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
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<Ignore, string> result, CancellationToken token)
    {
        string originalTopic = ReadStringHeader(result.Message.Headers, "x-original-topic") ?? "unknown";
        string retryCount = ReadStringHeader(result.Message.Headers, "x-retry-count") ?? "unknown";
        string exceptionType = ReadStringHeader(result.Message.Headers, "x-exception-type") ?? "unknown";
        string exceptionMessage = ReadStringHeader(result.Message.Headers, "x-exception-message") ?? "unknown";

        logger.LogCritical(
            "Notification landed in the DLQ. OriginalTopic={OriginalTopic} RetryCount={RetryCount} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage} Payload={Payload}",
            originalTopic,
            retryCount,
            exceptionType,
            exceptionMessage,
            result.Message.Value);

        try
        {
            DispatchNotificationRequest? request = JsonSerializer.Deserialize<DispatchNotificationRequest>(result.Message.Value, DeserializeOptions);
            if (request is null)
            {
                logger.LogWarning("DLQ message payload could not be deserialized - skipping status update.");
                return;
            }

            using IServiceScope scope = scopeFactory.CreateScope();
            INotificationRepository repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

            NotificationEntity? notification = await repository.GetByCorrelationIdAsync(request.CorrelationId, token);
            if (notification is null)
            {
                logger.LogWarning(
                    "No notification record found for CorrelationId={CorrelationId} - skipping status update.",
                    request.CorrelationId);
                return;
            }

            await repository.UpdateStatusAsync(notification.Id, NotificationStatus.Failed, exceptionMessage, token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error marking a DLQ'd notification as Failed.");
        }
    }

    private static string? ReadStringHeader(Headers headers, string key) =>
        headers.TryGetLastBytes(key, out byte[] bytes) ? Encoding.UTF8.GetString(bytes) : null;

    public void Dispose()
    {
        _consumeLoopCts?.Dispose();
        _consumer?.Dispose();
    }
}
