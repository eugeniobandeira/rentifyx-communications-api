namespace RentifyxCommunications.Application.Features.Consent.Handlers.Update.Request;

public sealed record UpdateConsentRequest(
    string RecipientId,
    string Channel,
    bool OptedIn);
