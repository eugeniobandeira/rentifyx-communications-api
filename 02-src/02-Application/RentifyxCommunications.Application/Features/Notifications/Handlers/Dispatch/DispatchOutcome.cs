using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;

public sealed record DispatchOutcome(NotificationStatus Status, bool WasDuplicate);
