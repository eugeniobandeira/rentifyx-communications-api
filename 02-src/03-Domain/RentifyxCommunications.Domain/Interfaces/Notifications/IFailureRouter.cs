using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface IFailureRouter
{
    Task RouteAsync(
        string rawMessage,
        RetryContext context,
        FailureClassification classification,
        string exceptionType,
        string exceptionMessage,
        CancellationToken cancellationToken = default);
}
