using System.Text.Json;
using Confluent.Kafka;
using ErrorOr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;

namespace RentifyxCommunications.Api.Messaging;

public sealed class NotificationRequestedConsumer(
    ILogger<NotificationRequestedConsumer> logger,
    IKafkaConsumerFactory consumerFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeSpan? startupRetryDelayOverride = null) : IHostedService, IDisposable
{
    internal const string Topic = "notification-requested";
    internal const int MaxStartupAttempts = 3;

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<NotificationRequestedConsumer> _logger = logger;
    private readonly IKafkaConsumerFactory _consumerFactory = consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly string _groupId = configuration["Kafka:ConsumerGroupId"] ?? "rentifyx-communications-api";
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

    private async Task ConsumeLoopAsync(IConsumer<Ignore, string> consumer, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ConsumeResult<Ignore, string>? result = consumer.Consume(TimeSpan.FromSeconds(1));

            if (result is null || result.IsPartitionEOF)
                continue;

            await ProcessMessageAsync(result, token);
            consumer.Commit(result);
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<Ignore, string> result, CancellationToken token)
    {
        DispatchNotificationRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<DispatchNotificationRequest>(result.Message.Value, DeserializeOptions)
                ?? throw new JsonException("Deserialized NotificationRequested message was null.");

            using IServiceScope scope = _scopeFactory.CreateScope();
            IHandler<DispatchNotificationRequest, DispatchOutcome> handler =
                scope.ServiceProvider.GetRequiredService<IHandler<DispatchNotificationRequest, DispatchOutcome>>();

            ErrorOr<DispatchOutcome> outcome = await handler.HandleAsync(request, token);

            if (outcome.IsError)
            {
                _logger.LogError(
                    "DispatchNotificationHandler returned errors. CorrelationId={CorrelationId} Errors={@Errors}",
                    request.CorrelationId,
                    outcome.Errors);
            }
            else
            {
                _logger.LogInformation(
                    "Notification processed. CorrelationId={CorrelationId} Status={Status} WasDuplicate={WasDuplicate}",
                    request.CorrelationId,
                    outcome.Value.Status,
                    outcome.Value.WasDuplicate);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Malformed NotificationRequested message. Partition={Partition} Offset={Offset} Payload={Payload}",
                result.Partition.Value,
                result.Offset.Value,
                result.Message.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error processing NotificationRequested message. CorrelationId={CorrelationId}",
                request?.CorrelationId);
        }
    }

    public void Dispose()
    {
        _consumeLoopCts?.Dispose();
        _consumer?.Dispose();
    }
}
