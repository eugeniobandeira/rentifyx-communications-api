using FluentValidation;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Request;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Validator;

public sealed class GetNotificationsByRecipientValidator : AbstractValidator<GetNotificationsByRecipientRequest>
{
    private const string RecipientIdRequiredErrorCode = "GetByRecipient.RecipientIdRequired";
    private const string RecipientIdInvalidErrorCode = "GetByRecipient.RecipientIdInvalid";

    public GetNotificationsByRecipientValidator()
    {
        RuleFor(x => x.RecipientId)
            .NotEmpty()
                .WithErrorCode(RecipientIdRequiredErrorCode)
                .WithMessage("Recipient id is required.")
            .Must(recipientId => Guid.TryParse(recipientId, out _))
                .WithErrorCode(RecipientIdInvalidErrorCode)
                .WithMessage("Recipient id must be a valid GUID.")
                .When(x => !string.IsNullOrWhiteSpace(x.RecipientId));
    }
}
