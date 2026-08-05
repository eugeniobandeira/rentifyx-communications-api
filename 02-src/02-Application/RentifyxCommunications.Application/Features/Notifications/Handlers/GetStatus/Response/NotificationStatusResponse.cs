using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Response;

public sealed record NotificationStatusResponse(
    Guid Id,
    Guid IdempotencyKey,
    Guid RecipientId,
    Channel Channel,
    NotificationStatus Status,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
