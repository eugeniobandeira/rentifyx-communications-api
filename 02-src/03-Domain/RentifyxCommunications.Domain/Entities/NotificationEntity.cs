using ErrorOr;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Events;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Entities;

public sealed class NotificationEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid RecipientId { get; private set; }
    public EmailAddress Recipient { get; private set; } = null!;
    public Channel Channel { get; private set; }
    public TemplateId TemplateId { get; private set; } = null!;
    public IReadOnlyDictionary<string, string> Payload { get; private set; } = null!;
    public NotificationStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private NotificationEntity() { }

    public static ErrorOr<NotificationEntity> Create(
        Guid correlationId,
        Guid recipientId,
        EmailAddress recipient,
        Channel channel,
        TemplateId templateId,
        IReadOnlyDictionary<string, string> payload)
    {
        if (payload is null || payload.Count == 0)
            return Error.Validation(NotificationErrorCodes.InvalidPayload, "Payload must not be empty.");

        return new NotificationEntity
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            RecipientId = recipientId,
            Recipient = recipient,
            Channel = channel,
            TemplateId = templateId,
            Payload = payload,
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public ErrorOr<Success> Dispatch(ConsentDecision consent, bool isPayloadValid)
    {
        if (Status != NotificationStatus.Pending)
            return Error.Validation(NotificationErrorCodes.InvalidTransition, $"Cannot dispatch a notification in '{Status}' status.");

        if (Channel is not Channel.Email)
            return Error.Validation(NotificationErrorCodes.ChannelNotImplemented, $"Channel '{Channel}' is not implemented.");

        if (!isPayloadValid)
            return Error.Validation(NotificationErrorCodes.InvalidPayload, "Payload does not satisfy the template's required fields.");

        if (consent.IsSuppressed)
        {
            Status = NotificationStatus.Suppressed;
            UpdatedAt = DateTime.UtcNow;
            _domainEvents.Add(new NotificationSuppressed(Id, CorrelationId, DateTime.UtcNow));
            return Result.Success;
        }

        Status = NotificationStatus.Dispatching;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new NotificationDispatched(Id, CorrelationId, DateTime.UtcNow));
        return Result.Success;
    }

    public ErrorOr<Success> MarkSent()
    {
        ErrorOr<Success> guard = EnsureDispatching();
        if (guard.IsError)
            return guard;

        Status = NotificationStatus.Sent;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new NotificationDelivered(Id, CorrelationId, DateTime.UtcNow));
        return Result.Success;
    }

    public ErrorOr<Success> MarkFailed(string reason)
    {
        ErrorOr<Success> guard = EnsureDispatching();
        if (guard.IsError)
            return guard;

        Status = NotificationStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new NotificationFailed(Id, CorrelationId, reason, DateTime.UtcNow));
        return Result.Success;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private ErrorOr<Success> EnsureDispatching()
    {
        if (Status is NotificationStatus.Sent or NotificationStatus.Failed or NotificationStatus.Suppressed)
            return Error.Validation(NotificationErrorCodes.AlreadyTerminal, $"Notification is already in a terminal status '{Status}'.");

        if (Status != NotificationStatus.Dispatching)
            return Error.Validation(NotificationErrorCodes.InvalidTransition, $"Cannot complete a notification in '{Status}' status.");

        return Result.Success;
    }
}
