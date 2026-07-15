using System.Diagnostics;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;

/// <summary>
/// Shared deserialize -> invoke handler -> classify -> route pipeline, reused by every stage's
/// consumer (the original topic and each retry stage) so the logic exists in exactly one place.
/// </summary>
public sealed class NotificationDispatchProcessor(
    IHandler<DispatchNotificationRequest, DispatchNotificationResponse> handler,
    IFailureRouter failureRouter,
    NotificationMetrics metrics,
    ILogger<NotificationDispatchProcessor> logger)
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ProcessAsync(string rawMessage, RetryContext context, CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await ProcessCoreAsync(rawMessage, context, cancellationToken);
        }
        finally
        {
            metrics.RecordDispatchDuration(stopwatch.Elapsed.TotalSeconds);
        }
    }

    private async Task ProcessCoreAsync(string rawMessage, RetryContext context, CancellationToken cancellationToken)
    {
        DispatchNotificationRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<DispatchNotificationRequest>(rawMessage, DeserializeOptions)
                ?? throw new JsonException("Deserialized NotificationRequested message was null.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Malformed NotificationRequested message on topic {Topic}.", context.OriginalTopic);
            await failureRouter.RouteAsync(rawMessage, context, FailureClassification.PoisonPill, nameof(JsonException), ex.Message, cancellationToken);
            return;
        }

        try
        {
            ErrorOr<DispatchNotificationResponse> outcome = await handler.HandleAsync(request, cancellationToken);

            if (outcome.IsError)
            {
                logger.LogError(
                    "DispatchNotificationHandler returned errors. CorrelationId={CorrelationId} Errors={@Errors}",
                    request.CorrelationId,
                    outcome.Errors);

                FailureClassification classification = FailureClassifier.Classify(outcome.Errors);
                Error firstError = outcome.FirstError;
                await failureRouter.RouteAsync(rawMessage, context, classification, firstError.Code, firstError.Description, cancellationToken);
                return;
            }

            logger.LogInformation(
                "Notification processed. CorrelationId={CorrelationId} Status={Status} WasDuplicate={WasDuplicate}",
                request.CorrelationId,
                outcome.Value.Status,
                outcome.Value.WasDuplicate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error processing NotificationRequested message. CorrelationId={CorrelationId}", request.CorrelationId);

            FailureClassification classification = FailureClassifier.Classify(ex);
            await failureRouter.RouteAsync(rawMessage, context, classification, ex.GetType().Name, ex.Message, cancellationToken);
        }
    }
}
