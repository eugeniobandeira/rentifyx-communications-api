using ErrorOr;
using FluentValidation;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Response;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces.Notifications;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus;

public sealed class GetNotificationStatusHandler(
    IValidator<GetNotificationStatusRequest> validator,
    INotificationRepository notificationRepository) : IHandler<GetNotificationStatusRequest, NotificationStatusResponse>
{
    public async Task<ErrorOr<NotificationStatusResponse>> HandleAsync(
        GetNotificationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        List<Error>? validationErrors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (validationErrors is not null)
        {
            return validationErrors;
        }

        Guid id = Guid.Parse(request.Id);

        NotificationEntity? notification = await notificationRepository.GetByIdAsync(id, cancellationToken);
        if (notification is null)
        {
            return Error.NotFound(
                NotificationErrorCodes.NotFound,
                $"Notification '{id}' was not found.");
        }

        return new NotificationStatusResponse(
            notification.Id,
            notification.CorrelationId,
            notification.RecipientId,
            notification.Channel,
            notification.Status,
            notification.FailureReason,
            notification.CreatedAt,
            notification.UpdatedAt);
    }
}
