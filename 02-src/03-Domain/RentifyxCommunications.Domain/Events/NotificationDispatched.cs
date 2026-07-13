namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationDispatched(Guid NotificationId, Guid CorrelationId, DateTime OccurredAt) : IDomainEvent;
