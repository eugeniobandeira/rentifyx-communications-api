using FluentValidation;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Request;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.MessageResource;

#pragma warning disable CA1716 // "Get" folder name mirrors the feature action, not a reserved-keyword API surface.
namespace RentifyxCommunications.Application.Features.Consent.Handlers.Get.Validator;
#pragma warning restore CA1716

public sealed class GetConsentValidator : AbstractValidator<GetConsentRequest>
{
    public GetConsentValidator()
    {
        RuleFor(x => x.RecipientId)
            .Must(recipientId => Guid.TryParse(recipientId, out _))
                .WithErrorCode(NotificationErrorCodes.InvalidRecipientId)
                .WithMessage(ValidationMessageResource.RECIPIENT_ID_REQUIRED);

        RuleFor(x => x.Channel)
            .Must(channel => Enum.TryParse<Channel>(channel, out _))
                .WithErrorCode(DispatchValidationErrorCodes.InvalidChannel)
                .WithMessage(ValidationMessageResource.CHANNEL_INVALID);
    }
}
