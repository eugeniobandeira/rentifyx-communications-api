using ErrorOr;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Email;

public sealed class MockEmailSender : IEmailSender
{
    private readonly List<(EmailAddress Recipient, string RenderedContent)> _sentEmails = [];

    public IReadOnlyList<(EmailAddress Recipient, string RenderedContent)> SentEmails => _sentEmails.AsReadOnly();

    public Task<ErrorOr<Success>> SendAsync(
        EmailAddress recipient,
        string renderedContent,
        CancellationToken cancellationToken = default)
    {
        _sentEmails.Add((recipient, renderedContent));
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}
