using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Response;

public sealed record NotificationListResponse(IReadOnlyList<NotificationListItem> Notifications);

public sealed record NotificationListItem(
    Guid Id,
    Channel Channel,
    NotificationStatus Status,
    DateTime CreatedAt);
