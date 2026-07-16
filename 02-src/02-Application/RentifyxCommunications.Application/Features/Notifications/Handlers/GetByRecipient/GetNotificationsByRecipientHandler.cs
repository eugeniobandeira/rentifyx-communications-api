using ErrorOr;
using FluentValidation;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Response;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces.Notifications;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient;

public sealed class GetNotificationsByRecipientHandler(
    IValidator<GetNotificationsByRecipientRequest> validator,
    INotificationRepository notificationRepository) : IHandler<GetNotificationsByRecipientRequest, NotificationListResponse>
{
    public async Task<ErrorOr<NotificationListResponse>> HandleAsync(
        GetNotificationsByRecipientRequest request,
        CancellationToken cancellationToken = default)
    {
        List<Error>? validationErrors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (validationErrors is not null)
        {
            return validationErrors;
        }

        Guid recipientId = Guid.Parse(request.RecipientId);

        IReadOnlyList<NotificationEntity> notifications = await notificationRepository.GetByRecipientAsync(recipientId, cancellationToken);

        IReadOnlyList<NotificationListItem> items = [.. notifications.Select(ToListItem)];

        return new NotificationListResponse(items);
    }

    private static NotificationListItem ToListItem(NotificationEntity notification) => new(
        notification.Id,
        notification.Channel,
        notification.Status,
        notification.CreatedAt);
}
