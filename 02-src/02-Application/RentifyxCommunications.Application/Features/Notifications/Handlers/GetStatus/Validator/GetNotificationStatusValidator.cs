using FluentValidation;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Request;
using RentifyxCommunications.Domain.Constants;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Validator;

public sealed class GetNotificationStatusValidator : AbstractValidator<GetNotificationStatusRequest>
{
    public GetNotificationStatusValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
                .WithErrorCode(NotificationErrorCodes.InvalidId)
                .WithMessage("Notification id is required.")
            .Must(id => Guid.TryParse(id, out _))
                .WithErrorCode(NotificationErrorCodes.InvalidId)
                .WithMessage("Notification id must be a valid GUID.")
                .When(x => !string.IsNullOrEmpty(x.Id));
    }
}
