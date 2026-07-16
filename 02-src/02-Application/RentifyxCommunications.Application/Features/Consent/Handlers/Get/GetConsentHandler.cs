using ErrorOr;
using FluentValidation;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Response;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

#pragma warning disable CA1716 // "Get" folder name mirrors the feature action, not a reserved-keyword API surface.
namespace RentifyxCommunications.Application.Features.Consent.Handlers.Get;
#pragma warning restore CA1716

public sealed class GetConsentHandler(
    IValidator<GetConsentRequest> validator,
    IConsentRepository consentRepository) : IHandler<GetConsentRequest, ConsentResponse>
{
    public async Task<ErrorOr<ConsentResponse>> HandleAsync(
        GetConsentRequest request,
        CancellationToken cancellationToken = default)
    {
        List<Error>? validationErrors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (validationErrors is not null)
        {
            return validationErrors;
        }

        Guid recipientId = Guid.Parse(request.RecipientId);
        _ = Enum.TryParse(request.Channel, out Channel channel);

        ConsentPreference? preference = await consentRepository.GetAsync(recipientId, channel, cancellationToken);

        if (preference is null)
        {
            ConsentDecision decision = ConsentDecision.NoRecordFound();
            return new ConsentResponse(recipientId, channel.ToString(), OptedIn: !decision.IsSuppressed, UpdatedAt: null);
        }

        ConsentDecision consentDecision = ConsentDecision.FromPreference(preference);
        return new ConsentResponse(recipientId, channel.ToString(), OptedIn: !consentDecision.IsSuppressed, preference.UpdatedAt);
    }
}
