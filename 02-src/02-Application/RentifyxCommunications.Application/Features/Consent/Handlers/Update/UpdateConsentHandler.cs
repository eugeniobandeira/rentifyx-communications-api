using ErrorOr;
using FluentValidation;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Response;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Application.Features.Consent.Handlers.Update;

public sealed class UpdateConsentHandler(
    IValidator<UpdateConsentRequest> validator,
    IConsentRepository consentRepository,
    IConsentAuditRepository consentAuditRepository) : IHandler<UpdateConsentRequest, ConsentResponse>
{
    public async Task<ErrorOr<ConsentResponse>> HandleAsync(
        UpdateConsentRequest request,
        CancellationToken cancellationToken = default)
    {
        List<Error>? validationErrors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (validationErrors is not null)
        {
            return validationErrors;
        }

        Guid recipientId = Guid.Parse(request.RecipientId);
        Enum.TryParse(request.Channel, ignoreCase: true, out Channel channel);

        ConsentPreference? previous = await consentRepository.GetAsync(recipientId, channel, cancellationToken);
        bool? previousOptedIn = previous?.OptedIn;

        DateTime changedAt = DateTime.UtcNow;

        ErrorOr<ConsentPreference> preferenceResult = ConsentPreference.Create(
            recipientId,
            channel,
            request.OptedIn,
            changedAt);
        if (preferenceResult.IsError)
        {
            return preferenceResult.Errors;
        }

        ConsentPreference preference = preferenceResult.Value;

        await consentRepository.UpdateAsync(preference, cancellationToken);

        try
        {
            ConsentAuditEntry auditEntry = new(
                recipientId,
                channel,
                previousOptedIn,
                request.OptedIn,
                changedAt);

            await consentAuditRepository.AddAsync(auditEntry, cancellationToken);
        }
        catch (Exception ex)
        {
            // The consent state is already durable at this point (UpdateAsync succeeded above) and is
            // NOT rolled back. We still surface this as an error rather than swallowing it: an audit-less
            // consent change is itself an LGPD compliance gap, so silently returning success would hide it.
            return Error.Unexpected(
                code: "Consent.AuditWriteFailed",
                description: $"Consent was updated but the audit trail could not be recorded: {ex.Message}");
        }

        return new ConsentResponse(recipientId, channel, request.OptedIn, changedAt);
    }
}
