using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Domain.Interfaces;

public interface INotificationRepository
{
    Task<bool> SaveIfNotExistsAsync(Notification notification, CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> GetByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, NotificationStatus status, CancellationToken cancellationToken = default);
}
