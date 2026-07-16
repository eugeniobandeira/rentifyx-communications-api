using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface IConsentRepository
{
    Task<ConsentPreference?> GetAsync(Guid recipientId, Channel channel, CancellationToken cancellationToken = default);

    Task UpdateAsync(ConsentPreference consent, CancellationToken cancellationToken = default);
}
