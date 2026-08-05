namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationDispatched(
    Guid NotificationId,
    Guid IdempotencyKey,
    DateTime OccurredAt) : IDomainEvent;
