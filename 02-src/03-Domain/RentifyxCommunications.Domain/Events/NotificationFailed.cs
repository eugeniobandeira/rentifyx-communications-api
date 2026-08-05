namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationFailed(
    Guid NotificationId,
    Guid IdempotencyKey,
    string Reason,
    DateTime OccurredAt) : IDomainEvent;
