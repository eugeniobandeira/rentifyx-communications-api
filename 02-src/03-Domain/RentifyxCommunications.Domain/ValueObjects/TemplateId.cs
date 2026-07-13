using ErrorOr;
using RentifyxCommunications.Domain.Constants;

namespace RentifyxCommunications.Domain.ValueObjects;

public sealed class TemplateId
{
    public string Value { get; }

    private TemplateId(string value)
    {
        Value = value;
    }

    public static ErrorOr<TemplateId> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation(NotificationErrorCodes.InvalidTemplateId, "Template id must not be empty.");

        return new TemplateId(value);
    }
}
