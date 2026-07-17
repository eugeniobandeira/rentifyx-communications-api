namespace RentifyxCommunications.Application.Features.Consent.Handlers.Update.Response;

public sealed record ConsentResponse(
    Guid RecipientId,
    string Channel,
    bool OptedIn,
    DateTime UpdatedAt);
