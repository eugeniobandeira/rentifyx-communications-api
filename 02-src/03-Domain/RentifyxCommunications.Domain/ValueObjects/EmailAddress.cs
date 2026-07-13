using System.Net.Mail;
using ErrorOr;
using RentifyxCommunications.Domain.Constants;

namespace RentifyxCommunications.Domain.ValueObjects;

public sealed class EmailAddress
{
    public string Value { get; }

    private EmailAddress(string value) => Value = value;

    public static ErrorOr<EmailAddress> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation(NotificationErrorCodes.InvalidEmailAddress, "Email address must not be empty.");

        try
        {
            MailAddress parsed = new(value);
            return new EmailAddress(parsed.Address);
        }
        catch (FormatException)
        {
            return Error.Validation(NotificationErrorCodes.InvalidEmailAddress, $"'{value}' is not a valid email address.");
        }
    }
}
