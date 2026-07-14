using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using ErrorOr;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Email;

public sealed class SesEmailSender(
    IAmazonSimpleEmailService client,
    ISecretsProvider secretsProvider,
    SecretsProviderOptions secretsOptions) : IEmailSender
{
    private const string DefaultSubject = "RentifyX Notification";

    public async Task<ErrorOr<Success>> SendAsync(
        EmailAddress recipient,
        string renderedContent,
        CancellationToken cancellationToken = default)
    {
        string senderArn = await secretsProvider.GetSecretAsync(secretsOptions.SesArn, cancellationToken);
        string fromAddress = ExtractIdentityFromArn(senderArn);

        try
        {
            await client.SendEmailAsync(new SendEmailRequest
            {
                Source = fromAddress,
                Destination = new Destination { ToAddresses = [recipient.Value] },
                Message = new Message
                {
                    Subject = new Content(DefaultSubject),
                    Body = new Body { Text = new Content(renderedContent) }
                }
            }, cancellationToken);

            return Result.Success;
        }
        catch (AmazonSimpleEmailServiceException ex)
        {
            return Error.Failure("Ses.SendFailed", ex.Message);
        }
    }

    private static string ExtractIdentityFromArn(string arn)
    {
        int separatorIndex = arn.LastIndexOf('/');
        return separatorIndex >= 0 ? arn[(separatorIndex + 1)..] : arn;
    }
}
