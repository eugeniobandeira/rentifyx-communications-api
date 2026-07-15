using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface INotificationRepository
{
    Task<bool> SaveIfNotExistsAsync(NotificationEntity notification, CancellationToken cancellationToken = default);

    Task<NotificationEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NotificationEntity?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationEntity>> GetByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, NotificationStatus status, string? failureReason = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationEntity>> GetStuckDispatchingAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}
