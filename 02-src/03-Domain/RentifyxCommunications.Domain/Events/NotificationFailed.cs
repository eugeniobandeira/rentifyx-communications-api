namespace RentifyxCommunications.Domain.Events;

public sealed record NotificationFailed(Guid NotificationId, Guid CorrelationId, string Reason, DateTime OccurredAt) : IDomainEvent;
