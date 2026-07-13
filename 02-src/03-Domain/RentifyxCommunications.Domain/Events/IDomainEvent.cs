namespace RentifyxCommunications.Domain.Events;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
