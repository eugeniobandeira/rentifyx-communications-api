using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface IConsentAuditRepository
{
    Task AddAsync(ConsentAuditEntry entry, CancellationToken cancellationToken = default);
}
