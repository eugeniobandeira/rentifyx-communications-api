namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationDelivered(
    Guid NotificationId,
    Guid IdempotencyKey,
    DateTime OccurredAt) : IDomainEvent;
