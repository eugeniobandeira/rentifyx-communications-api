namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationSuppressed(Guid NotificationId, Guid CorrelationId, DateTime OccurredAt) : IDomainEvent;
