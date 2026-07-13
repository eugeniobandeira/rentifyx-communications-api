using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface IConsentRepository
{
    Task<ConsentPreference?> FindAsync(Guid recipientId, Channel channel, CancellationToken cancellationToken = default);
}
