namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationDelivered(
    Guid NotificationId,
    Guid CorrelationId,
    DateTime OccurredAt) : IDomainEvent;
