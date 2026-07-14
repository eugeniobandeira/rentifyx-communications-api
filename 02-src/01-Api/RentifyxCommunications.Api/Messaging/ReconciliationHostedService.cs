using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Options;

namespace RentifyxCommunications.Api.Messaging;

public sealed class ReconciliationHostedService(
    ILogger<ReconciliationHostedService> logger,
    IServiceScopeFactory scopeFactory,
    ReconciliationOptions options) : IHostedService, IDisposable
{
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PollIntervalSeconds));
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_timer, _loopCts.Token), CancellationToken.None);

        logger.LogInformation(
            "ReconciliationHostedService started. PollIntervalSeconds={PollIntervalSeconds} StalenessThresholdSeconds={StalenessThresholdSeconds}",
            options.PollIntervalSeconds,
            options.StalenessThresholdSeconds);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_loopCts is not null)
            await _loopCts.CancelAsync();

        if (_loopTask is not null)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                await _loopTask.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                // Best-effort drain within the timeout.
            }
        }

        _loopCts?.Dispose();
        logger.LogInformation("ReconciliationHostedService stopped");
    }

    private async Task LoopAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await ReconcileOnceAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken token)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        INotificationRepository repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        IFailureRouter router = scope.ServiceProvider.GetRequiredService<IFailureRouter>();

        IReadOnlyList<NotificationEntity> stuck = await repository.GetStuckDispatchingAsync(
            TimeSpan.FromSeconds(options.StalenessThresholdSeconds),
            token);

        foreach (NotificationEntity notification in stuck)
        {
            try
            {
                string rawMessage = BuildRawMessage(notification);
                RetryContext context = new(RetryTopicChain.OriginalTopic);

                await router.RouteAsync(
                    rawMessage,
                    context,
                    FailureClassification.Transient,
                    nameof(ReconciliationHostedService),
                    "Notification stuck in Dispatching status - republished for another attempt.",
                    token);

                logger.LogWarning(
                    "Republished stuck notification. Id={Id} CorrelationId={CorrelationId}",
                    notification.Id,
                    notification.CorrelationId);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to republish stuck notification. Id={Id} CorrelationId={CorrelationId}",
                    notification.Id,
                    notification.CorrelationId);
            }
        }
    }

    private static string BuildRawMessage(NotificationEntity notification)
    {
        DispatchNotificationRequest request = new(
            notification.CorrelationId,
            notification.RecipientId,
            notification.Recipient.Value,
            notification.Channel.ToString(),
            notification.TemplateId.Value,
            notification.Payload);

        return JsonSerializer.Serialize(request);
    }

    public void Dispose()
    {
        _loopCts?.Dispose();
        _timer?.Dispose();
    }
}
