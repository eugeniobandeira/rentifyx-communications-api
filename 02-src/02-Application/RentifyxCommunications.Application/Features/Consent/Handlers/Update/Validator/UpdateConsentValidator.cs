using FluentValidation;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Request;
using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Consent.Handlers.Update.Validator;

public sealed class UpdateConsentValidator : AbstractValidator<UpdateConsentRequest>
{
    public UpdateConsentValidator()
    {
        RuleFor(x => x.RecipientId)
            .Must(recipientId => Guid.TryParse(recipientId, out Guid parsed) && parsed != Guid.Empty)
                .WithErrorCode("Consent.InvalidRecipientId")
                .WithMessage("Recipient id must be a well-formed, non-empty GUID.");

        RuleFor(x => x.Channel)
            .Must(channel => Enum.TryParse<Channel>(channel, ignoreCase: true, out _))
                .WithErrorCode("Consent.InvalidChannel")
                .WithMessage("Channel '{PropertyValue}' is not recognized.");
    }
}
