using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;

public sealed record DispatchNotificationResponse(NotificationStatus Status, bool WasDuplicate);
