using FluentValidation;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.MessageResource;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Validator;

public sealed class DispatchNotificationValidator : AbstractValidator<DispatchNotificationRequest>
{
    public DispatchNotificationValidator()
    {
        RuleFor(x => x.CorrelationId)
            .NotEqual(Guid.Empty)
                .WithErrorCode(DispatchValidationErrorCodes.CorrelationIdRequired)
                .WithMessage(ValidationMessageResource.CORRELATION_ID_REQUIRED);

        RuleFor(x => x.RecipientId)
            .NotEqual(Guid.Empty)
                .WithErrorCode(DispatchValidationErrorCodes.RecipientIdRequired)
                .WithMessage(ValidationMessageResource.RECIPIENT_ID_REQUIRED);

        RuleFor(x => x.RecipientEmail)
            .NotEmpty()
                .WithErrorCode(DispatchValidationErrorCodes.RecipientEmailRequired)
                .WithMessage(ValidationMessageResource.RECIPIENT_EMAIL_REQUIRED);

        RuleFor(x => x.TemplateId)
            .NotEmpty()
                .WithErrorCode(DispatchValidationErrorCodes.TemplateIdRequired)
                .WithMessage(ValidationMessageResource.TEMPLATE_ID_REQUIRED);

        RuleFor(x => x.Channel)
            .Must(channel => Enum.TryParse<Channel>(channel, ignoreCase: true, out _))
                .WithErrorCode(DispatchValidationErrorCodes.InvalidChannel)
                .WithMessage(ValidationMessageResource.CHANNEL_INVALID);

        RuleFor(x => x.Payload)
            .Must(payload => payload is { Count: > 0 })
                .WithErrorCode(DispatchValidationErrorCodes.PayloadRequired)
                .WithMessage(ValidationMessageResource.PAYLOAD_REQUIRED);
    }
}
