using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Domain.ValueObjects;

public sealed record ConsentAuditEntry(
    Guid RecipientId,
    Channel Channel,
    bool? PreviousOptedIn,
    bool NewOptedIn,
    DateTime ChangedAt);
