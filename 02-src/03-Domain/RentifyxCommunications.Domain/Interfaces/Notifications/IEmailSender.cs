using ErrorOr;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface IEmailSender
{
    Task<ErrorOr<Success>> SendAsync(
        EmailAddress recipient,
        string renderedContent,
        CancellationToken cancellationToken = default);
}
