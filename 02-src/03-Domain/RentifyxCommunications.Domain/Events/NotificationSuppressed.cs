namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationSuppressed(
    Guid NotificationId,
    Guid IdempotencyKey,
    DateTime OccurredAt) : IDomainEvent;
