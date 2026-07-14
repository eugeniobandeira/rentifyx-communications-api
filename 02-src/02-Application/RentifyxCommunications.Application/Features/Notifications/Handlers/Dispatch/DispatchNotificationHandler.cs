using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;

public sealed class DispatchNotificationHandler(
    IValidator<DispatchNotificationRequest> validator,
    INotificationRepository notificationRepository,
    IConsentRepository consentRepository,
    ITemplateRenderer templateRenderer,
    IEmailSender emailSender,
    ILogger<DispatchNotificationHandler> logger) : IHandler<DispatchNotificationRequest, DispatchNotificationResponse>
{
    public async Task<ErrorOr<DispatchNotificationResponse>> HandleAsync(
        DispatchNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        List<Error>? validationErrors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (validationErrors is not null)
        {
            return validationErrors;
        }

        Enum.TryParse(request.Channel, ignoreCase: true, out Channel channel);

        ErrorOr<EmailAddress> emailResult = EmailAddress.Create(request.RecipientEmail);
        if (emailResult.IsError)
        {
            return emailResult.Errors;
        }

        ErrorOr<TemplateId> templateIdResult = TemplateId.Create(request.TemplateId);
        if (templateIdResult.IsError)
        {
            return templateIdResult.Errors;
        }

        ErrorOr<NotificationEntity> notificationResult = NotificationEntity.Create(
            request.CorrelationId,
            request.RecipientId,
            emailResult.Value,
            channel,
            templateIdResult.Value,
            request.Payload);
        if (notificationResult.IsError)
        {
            return notificationResult.Errors;
        }

        NotificationEntity notification = notificationResult.Value;

        bool saved = await notificationRepository.SaveIfNotExistsAsync(notification, cancellationToken);
        if (!saved)
        {
            logger.LogInformation(
                "Duplicate NotificationRequested message. CorrelationId={CorrelationId}",
                request.CorrelationId);
            return new DispatchNotificationResponse(NotificationStatus.Pending, WasDuplicate: true);
        }

        ConsentPreference? preference = await consentRepository.FindAsync(request.RecipientId, channel, cancellationToken);
        ConsentDecision consent = preference is null
            ? ConsentDecision.NoRecordFound()
            : ConsentDecision.FromPreference(preference);

        ErrorOr<Success> dispatchResult = notification.Dispatch(consent, isPayloadValid: true);
        if (dispatchResult.IsError)
        {
            return dispatchResult.Errors;
        }

        if (notification.Status == NotificationStatus.Suppressed)
        {
            await notificationRepository.UpdateStatusAsync(notification.Id, NotificationStatus.Suppressed, cancellationToken);
            logger.LogInformation(
                "Notification suppressed - recipient opted out. CorrelationId={CorrelationId}",
                request.CorrelationId);
            return new DispatchNotificationResponse(NotificationStatus.Suppressed, WasDuplicate: false);
        }

        ErrorOr<string> renderResult = await templateRenderer.RenderAsync(notification.TemplateId, notification.Payload, cancellationToken);
        if (renderResult.IsError)
        {
            notification.MarkFailed(renderResult.FirstError.Description);
            await notificationRepository.UpdateStatusAsync(notification.Id, NotificationStatus.Failed, cancellationToken);
            return new DispatchNotificationResponse(NotificationStatus.Failed, WasDuplicate: false);
        }

        await notificationRepository.UpdateStatusAsync(notification.Id, NotificationStatus.Dispatching, cancellationToken);

        ErrorOr<Success> sendResult = await emailSender.SendAsync(notification.Recipient, renderResult.Value, cancellationToken);
        if (sendResult.IsError)
        {
            notification.MarkFailed(sendResult.FirstError.Description);
            await notificationRepository.UpdateStatusAsync(notification.Id, NotificationStatus.Failed, cancellationToken);
            return new DispatchNotificationResponse(NotificationStatus.Failed, WasDuplicate: false);
        }

        notification.MarkSent();
        await notificationRepository.UpdateStatusAsync(notification.Id, NotificationStatus.Sent, cancellationToken);
        return new DispatchNotificationResponse(NotificationStatus.Sent, WasDuplicate: false);
    }
}
